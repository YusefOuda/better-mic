// Smoke test: links against the static webrtc-audio-processing build and checks
// that AEC3 actually removes echo on synthetic 48 kHz mono, 10 ms frames.
//
// Render (speaker) = speech-like signal (low-passed noise gated by a ~4 Hz
// "syllable" envelope; pure tones are a pathological AEC input). Capture (mic)
// = that signal delayed by 40 ms, attenuated, plus white noise -- i.e. pure
// echo, no near-end speech.
//
// The same input is processed twice, with AEC off and on. NS and AGC2 stay off
// so the difference is attributable to AEC alone (NS also attenuates this
// noise-like "echo", which would mask whether AEC works).

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <random>
#include <vector>

#include "api/audio/audio_processing.h"

namespace {

constexpr int kRate = 48000;
constexpr int kFrame = kRate / 100;  // 10 ms
constexpr int kSeconds = 8;
constexpr int kTotalFrames = kSeconds * 100;
constexpr int kDelayFrames = 4;  // 40 ms acoustic path

double ToDb(double e) { return 10.0 * std::log10(e + 1e-20); }

struct Result {
  bool ok = false;
  double in_db = 0, out_db = 0;
  webrtc::AudioProcessingStats stats;
};

// Energies are measured over the last 2 seconds, after AEC convergence.
Result Run(bool aec) {
  Result r;
  webrtc::AudioProcessing::Config cfg;
  cfg.echo_canceller.enabled = aec;
  cfg.echo_canceller.mobile_mode = false;  // AEC3
  cfg.high_pass_filter.enabled = true;

  rtc::scoped_refptr<webrtc::AudioProcessing> apm =
      webrtc::AudioProcessingBuilder().Create();
  if (!apm) {
    std::printf("FAIL: AudioProcessingBuilder().Create() returned null\n");
    return r;
  }
  apm->ApplyConfig(cfg);

  webrtc::StreamConfig stream(kRate, 1);
  std::mt19937 rng(1234);
  std::normal_distribution<float> noise(0.0f, 0.003f);
  std::normal_distribution<float> excitation(0.0f, 1.0f);
  float lp = 0.0f;

  std::vector<std::vector<float>> render_history;
  std::vector<float> render(kFrame), capture(kFrame), out(kFrame);
  double in_e = 0, out_e = 0;
  long n = 0;

  for (int f = 0; f < kTotalFrames; ++f) {
    for (int i = 0; i < kFrame; ++i) {
      double t = double(f * kFrame + i) / kRate;
      lp += 0.15f * (excitation(rng) - lp);  // one-pole low-pass, ~1.2 kHz
      double env = std::max(0.0, std::sin(2 * M_PI * 4.0 * t)) *
                   (0.6 + 0.4 * std::sin(2 * M_PI * 0.37 * t));
      render[i] = float(0.6 * env * lp);
    }
    render_history.push_back(render);

    const std::vector<float>* echo =
        f >= kDelayFrames ? &render_history[f - kDelayFrames] : nullptr;
    for (int i = 0; i < kFrame; ++i)
      capture[i] = (echo ? 0.5f * (*echo)[i] : 0.0f) + noise(rng);

    const float* rsrc[] = {render.data()};
    float* rdst[] = {render.data()};
    if (int err = apm->ProcessReverseStream(rsrc, stream, stream, rdst)) {
      std::printf("FAIL: ProcessReverseStream error %d\n", err);
      return r;
    }

    apm->set_stream_delay_ms(kDelayFrames * 10);
    const float* csrc[] = {capture.data()};
    float* cdst[] = {out.data()};
    if (int err = apm->ProcessStream(csrc, stream, stream, cdst)) {
      std::printf("FAIL: ProcessStream error %d\n", err);
      return r;
    }

    if (f >= kTotalFrames - 200) {
      for (int i = 0; i < kFrame; ++i) {
        in_e += double(capture[i]) * capture[i];
        out_e += double(out[i]) * out[i];
      }
      n += kFrame;
    }
  }

  r.ok = true;
  r.in_db = ToDb(in_e / n);
  r.out_db = ToDb(out_e / n);
  r.stats = apm->GetStatistics();
  return r;
}

}  // namespace

int main() {
  std::printf("%d frames per run (%d s @ %d Hz, 10 ms)\n\n", kTotalFrames,
              kSeconds, kRate);

  Result off = Run(false);
  Result on = Run(true);
  if (!off.ok || !on.ok) return 1;

  std::printf("                  AEC off   AEC on\n");
  std::printf("mic input  dBFS  %7.1f  %7.1f\n", off.in_db, on.in_db);
  std::printf("output     dBFS  %7.1f  %7.1f\n", off.out_db, on.out_db);
  std::printf("reduction  dB    %7.1f  %7.1f\n", off.in_db - off.out_db,
              on.in_db - on.out_db);
  if (on.stats.echo_return_loss_enhancement)
    std::printf("\nAEC ERLE (stats): %.1f dB\n", *on.stats.echo_return_loss_enhancement);
  if (on.stats.delay_ms)
    std::printf("AEC est. delay  : %d ms (actual %d ms)\n", *on.stats.delay_ms,
                kDelayFrames * 10);

  const double aec_gain = off.out_db - on.out_db;
  const bool pass = aec_gain > 15.0;
  std::printf("\nAEC improvement over baseline: %.1f dB -> %s\n", aec_gain,
              pass ? "PASS" : "FAIL (expected > 15 dB)");
  return pass ? 0 : 1;
}
