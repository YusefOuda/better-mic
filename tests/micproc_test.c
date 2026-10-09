/* Tests micproc.dll through its public C API (also checks the header is valid C). */
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#include "micproc.h"

#define N MICPROC_FRAME_SIZE
#define PI 3.14159265358979323846

static int failures = 0;

#define CHECK(cond, ...)                         \
  do {                                           \
    if (cond) {                                  \
      printf("  ok    ");                        \
    } else {                                     \
      printf("  FAIL  ");                        \
      failures++;                                \
    }                                            \
    printf(__VA_ARGS__);                         \
    printf("\n");                                \
  } while (0)

/* Deterministic xorshift + Box-Muller, so results don't depend on the CRT's rand(). */
static unsigned int rng_state = 12345;
static float uniform(void) {
  rng_state ^= rng_state << 13;
  rng_state ^= rng_state >> 17;
  rng_state ^= rng_state << 5;
  return (rng_state + 0.5f) / 4294967296.0f;
}
static float gauss(void) {
  return (float)(sqrt(-2.0 * log(uniform())) * cos(2 * PI * uniform()));
}

static double to_db(double e) { return 10.0 * log10(e + 1e-20); }

static double now_us(void) {
  struct timespec ts;
  timespec_get(&ts, TIME_UTC);
  return ts.tv_sec * 1e6 + ts.tv_nsec / 1e3;
}

static micproc_config make_config(int aec, int ns, int agc) {
  micproc_config c;
  micproc_default_config(&c);
  c.aec_enabled = aec;
  c.ns_mode = ns;
  c.agc_enabled = agc;
  return c;
}

static void test_api(void) {
  printf("api\n");
  CHECK(micproc_api_version() == MICPROC_API_VERSION, "api version %d", micproc_api_version());

  micproc_config c;
  micproc_default_config(&c);
  CHECK(c.aec_enabled && c.ns_mode == MICPROC_NS_RNNOISE && c.ns_level == 3 && c.agc_enabled &&
            c.hpf_enabled,
        "default config");

  micproc* p = micproc_create(NULL);
  CHECK(p != NULL, "create with defaults");

  float frame[N] = {0};
  CHECK(micproc_process_capture(NULL, frame) == MICPROC_ERR_NULL, "null handle rejected");
  CHECK(micproc_process_capture(p, NULL) == MICPROC_ERR_NULL, "null buffer rejected");
  CHECK(micproc_process_render(p, frame, 0) == MICPROC_ERR_ARG, "0 render channels rejected");
  CHECK(micproc_process_render(p, frame, 9) == MICPROC_ERR_ARG, "9 render channels rejected");
  CHECK(micproc_set_stream_delay_ms(p, -1) == MICPROC_ERR_ARG, "negative delay rejected");

  micproc_config bad = c;
  bad.ns_mode = 7;
  CHECK(micproc_set_config(p, &bad) == MICPROC_ERR_ARG, "invalid ns_mode rejected");
  CHECK(micproc_create(&bad) == NULL, "create with invalid config fails");

  micproc_config w = make_config(0, MICPROC_NS_WEBRTC, 0);
  w.ns_level = 3;
  micproc_config back;
  CHECK(micproc_set_config(p, &w) == MICPROC_OK && micproc_get_config(p, &back) == MICPROC_OK &&
            memcmp(&w, &back, sizeof w) == 0,
        "set/get config round-trips");

  micproc_stats s;
  CHECK(micproc_get_stats(p, &s) == MICPROC_OK && s.speech_probability == -1.0f,
        "speech_probability is -1 outside RNNoise mode");

  micproc_config d = make_config(0, MICPROC_NS_DEEPFILTER, 0);
  for (int level = 0; level <= 3; ++level) {
    d.ns_level = level;
    if (micproc_set_config(p, &d) != MICPROC_OK) break;
    if (level == 3) CHECK(1, "switching to DeepFilter at levels 0-3 succeeds");
  }
  if (d.ns_level != 3) CHECK(0, "switching to DeepFilter failed at level %d", d.ns_level);
  micproc_destroy(p);
  micproc_destroy(NULL); /* must be a no-op */
}

/* Speech-like signal: low-passed noise gated by a ~4 Hz syllable envelope. */
static float speechish(long n, float* lp) {
  double t = (double)n / MICPROC_SAMPLE_RATE;
  double env = sin(2 * PI * 4.0 * t);
  if (env < 0) env = 0;
  env *= 0.6 + 0.4 * sin(2 * PI * 0.37 * t);
  *lp += 0.15f * (gauss() - *lp);
  return (float)(0.6 * env * *lp);
}

/* Pure echo through the full wrapper, stereo render. Returns output energy (dBFS)
 * over the last 2 s; *in_db receives the mic input energy. */
static double run_echo(int aec, double* in_db, micproc_stats* stats) {
  enum { SECONDS = 8, FRAMES = SECONDS * 100, DELAY = 4 };
  micproc_config c = make_config(aec, MICPROC_NS_OFF, 0);
  micproc* p = micproc_create(&c);
  micproc_set_stream_delay_ms(p, DELAY * 10);

  static float history[FRAMES][N];
  float render[2 * N], mic[N], lp = 0;
  double ein = 0, eout = 0;
  rng_state = 777;
  for (int f = 0; f < FRAMES; ++f) {
    for (int i = 0; i < N; ++i) {
      float s = speechish((long)f * N + i, &lp);
      history[f][i] = s;
      render[2 * i] = s; /* same signal on L and R */
      render[2 * i + 1] = s;
    }
    for (int i = 0; i < N; ++i)
      mic[i] = (f >= DELAY ? 0.5f * history[f - DELAY][i] : 0.0f) + 0.003f * gauss();
    if (f >= FRAMES - 200)
      for (int i = 0; i < N; ++i) ein += (double)mic[i] * mic[i];

    micproc_process_render(p, render, 2);
    micproc_process_capture(p, mic);
    if (f >= FRAMES - 200)
      for (int i = 0; i < N; ++i) eout += (double)mic[i] * mic[i];
  }
  if (stats) micproc_get_stats(p, stats);
  micproc_destroy(p);
  *in_db = to_db(ein / (200.0 * N));
  return to_db(eout / (200.0 * N));
}

static void test_aec(void) {
  printf("aec (pure echo, stereo render, NS/AGC off)\n");
  double in_off, in_on;
  micproc_stats s;
  double out_off = run_echo(0, &in_off, NULL);
  double out_on = run_echo(1, &in_on, &s);
  printf("        input %.1f dBFS, output AEC off %.1f / on %.1f dBFS\n", in_on, out_off, out_on);
  printf("        stats: delay %d ms, ERLE %.1f dB\n", s.aec_delay_ms, s.aec_erle_db);
  CHECK(out_off - out_on > 15.0, "AEC removes echo: %.1f dB better than AEC off",
        out_off - out_on);
}

static void test_noise(int mode, const char* name) {
  printf("%s (stationary white noise only)\n", name);
  micproc_config c = make_config(0, mode, 0);
  c.hpf_enabled = 0;
  micproc* p = micproc_create(&c);
  float frame[N];
  double ein = 0, eout = 0, prob_sum = 0;
  int measured = 0;
  rng_state = 4242;
  for (int f = 0; f < 500; ++f) {
    for (int i = 0; i < N; ++i) frame[i] = 0.02f * gauss();
    int last = f >= 300;
    if (last) for (int i = 0; i < N; ++i) ein += (double)frame[i] * frame[i];
    micproc_process_capture(p, frame);
    if (last) {
      micproc_stats s;
      for (int i = 0; i < N; ++i) eout += (double)frame[i] * frame[i];
      micproc_get_stats(p, &s);
      prob_sum += s.speech_probability;
      measured++;
    }
  }
  micproc_destroy(p);
  double reduction = to_db(ein) - to_db(eout);
  double prob = prob_sum / measured;
  CHECK(reduction > 15.0, "noise reduced by %.1f dB", reduction);
  if (mode != MICPROC_NS_RNNOISE) return;
  /* Only a range check: RNNoise's VAD is trained on real speech and is not
   * meaningful on synthetic input (white noise scores ~0.6, a harmonic
   * voice-like tone ~0.0). Judge it on real recordings instead. */
  CHECK(prob >= 0.0 && prob <= 1.0, "speech probability in [0, 1] (mean %.2f)", prob);
}

static void test_agc_runs(void) {
  printf("agc\n");
  micproc_config c = make_config(0, MICPROC_NS_OFF, 1);
  micproc* p = micproc_create(&c);
  float frame[N], lp = 0;
  int finite = 1, err = 0;
  for (int f = 0; f < 300; ++f) {
    for (int i = 0; i < N; ++i) frame[i] = 0.05f * speechish((long)f * N + i, &lp);
    err |= micproc_process_capture(p, frame);
    for (int i = 0; i < N; ++i)
      if (!isfinite(frame[i]) || fabsf(frame[i]) > 1.0f) finite = 0;
  }
  micproc_destroy(p);
  CHECK(err == 0 && finite, "AGC2 runs, output finite and within [-1, 1]");
}

static void test_cost(int mode, const char* name) {
  printf("cost (full pipeline: AEC + %s + AGC, stereo render)\n", name);
  micproc_config c = make_config(1, mode, 1);
  micproc* p = micproc_create(&c);
  float render[2 * N], mic[N], lp = 0;
  enum { FRAMES = 3000 }; /* 30 s of audio */
  int full = 0; /* DeepFilter frames in its full-processing SNR band (-10..30 dB) */
  double full_us = 0, full_max_us = 0;
  /* Mic: voiced, voice-like signal (140 Hz harmonics with a formant boost and a
   * syllable envelope) plus background noise. DeepFilterNet skips its network
   * for frames it judges to be pure noise or clean speech, so a noise-only
   * input would understate its cost. */
  clock_t t0 = clock();
  for (int f = 0; f < FRAMES; ++f) {
    for (int i = 0; i < N; ++i) {
      long n = (long)f * N + i;
      double t = (double)n / MICPROC_SAMPLE_RATE, v = 0;
      for (int h = 1; h <= 25; ++h)
        v += sin(2 * PI * 140 * h * t) / h * (h * 140 > 300 && h * 140 < 900 ? 3 : 1);
      double env = 0.5 + 0.5 * sin(2 * PI * 3 * t);
      float s = speechish(n, &lp); /* game audio on the speakers */
      render[2 * i] = render[2 * i + 1] = s;
      mic[i] = (float)(0.03 * v * env) + 0.01f * gauss();
    }
    micproc_process_render(p, render, 2);
    double c0 = now_us();
    micproc_process_capture(p, mic);
    double cap_us = now_us() - c0;
    micproc_stats st;
    micproc_get_stats(p, &st);
    if (st.ns_snr_db > -10.0f && st.ns_snr_db < 30.0f) {
      full++;
      full_us += cap_us;
      if (cap_us > full_max_us) full_max_us = cap_us;
    }
  }
  double sec = (double)(clock() - t0) / CLOCKS_PER_SEC;
  micproc_destroy(p);
  double us_per_frame = sec * 1e6 / FRAMES;
  printf("        %.0f us per 10 ms frame (%.1f%% of one core)\n", us_per_frame,
         us_per_frame / 100.0);
  if (mode == MICPROC_NS_DEEPFILTER && full > 0)
    printf("        %.0f%% of frames took DeepFilterNet's full path: capture %.0f us mean, "
           "%.0f us max\n",
           100.0 * full / FRAMES, full_us / full, full_max_us);
  CHECK(us_per_frame < 5000.0, "well under real time (budget 10000 us)");
}

int main(void) {
  test_api();
  test_aec();
  test_noise(MICPROC_NS_RNNOISE, "rnnoise");
  test_noise(MICPROC_NS_DEEPFILTER, "deepfilter");
  test_agc_runs();
  test_cost(MICPROC_NS_RNNOISE, "RNNoise");
  test_cost(MICPROC_NS_DEEPFILTER, "DeepFilterNet");
  printf("\n%s (%d failure%s)\n", failures ? "FAILED" : "PASSED", failures,
         failures == 1 ? "" : "s");
  return failures ? 1 : 0;
}
