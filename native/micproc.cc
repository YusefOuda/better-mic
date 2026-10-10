#include "micproc.h"

#include <atomic>
#include <cstddef>
#include <cmath>
#include <limits>
#include <mutex>
#include <new>

#include "api/audio/audio_processing.h"
#include "rnnoise.h"

// native/dfshim (Rust): DeepFilterNet3 with the model embedded.
extern "C" {
struct DfShim;
DfShim* dfs_create(float atten_lim_db);
void dfs_destroy(DfShim* st);
size_t dfs_frame_length(const DfShim* st);
int32_t dfs_set_atten_lim(DfShim* st, float atten_lim_db);
float dfs_process(DfShim* st, const float* input, float* output);
}

static_assert(sizeof(micproc_config) == 5 * sizeof(int32_t), "micproc_config ABI");

namespace {

using webrtc::AudioProcessing;

constexpr int kMaxRenderChannels = 8;
constexpr float kRnnoiseScale = 32768.0f;  // RNNoise works in int16 range

// ns_level -> DeepFilterNet attenuation limit. Capping attenuation keeps a
// little background and sounds more natural; 100 dB means no cap.
constexpr float kDeepFilterAttenDb[] = {12.0f, 18.0f, 30.0f, 100.0f};

// ns_level -> share of the original mixed back into RNNoise's output (the same
// 12/18/30 dB limits; level 3 = pure RNNoise). Without a floor RNNoise gates the
// gaps between words to near silence, which sounds choppy, especially once AGC
// raises the speech around it.
constexpr float kRnnoiseDryMix[] = {0.25118864f, 0.12589254f, 0.03162278f, 0.0f};
// RNNoise's output lags its input by two frames (measured: 960 samples)
constexpr int kRnnoiseDelayFrames = 2;

bool ValidConfig(const micproc_config& c) {
  return c.ns_mode >= MICPROC_NS_OFF && c.ns_mode <= MICPROC_NS_DEEPFILTER &&
         c.ns_level >= 0 && c.ns_level <= 3;
}

}  // namespace

// Two APM instances keep the order echo -> noise -> gain even when the noise
// stage (RNNoise, DeepFilterNet) runs outside APM:
//   pre:  high-pass, AEC3, WebRTC NS (when selected)
//   post: AGC2 adaptive digital gain + limiter
struct micproc {
  rtc::scoped_refptr<AudioProcessing> pre;
  rtc::scoped_refptr<AudioProcessing> post;
  DenoiseState* rnnoise = nullptr;
  DfShim* deepfilter = nullptr;  // created on first use (model load is slow)

  std::mutex capture_mutex;  // guards config and capture-path state
  micproc_config config{};
  float speech_probability = -1.0f;
  float ns_snr_db = std::numeric_limits<float>::quiet_NaN();
  float scratch[MICPROC_FRAME_SIZE];
  // RNNoise input history, so the dry signal mixed back in lines up with its output
  float rnnoise_dry[kRnnoiseDelayFrames][MICPROC_FRAME_SIZE] = {};
  int rnnoise_dry_pos = 0;

  std::atomic<bool> aec_enabled{false};  // read on the render thread
  std::atomic<int32_t> stream_delay_ms{0};

  ~micproc() {
    if (rnnoise) rnnoise_destroy(rnnoise);
    dfs_destroy(deepfilter);
  }

  bool Apply(const micproc_config& c) {
    if (c.ns_mode == MICPROC_NS_DEEPFILTER) {
      const float atten = kDeepFilterAttenDb[c.ns_level];
      if (!deepfilter) {
        deepfilter = dfs_create(atten);
        if (!deepfilter || dfs_frame_length(deepfilter) != MICPROC_FRAME_SIZE) {
          dfs_destroy(deepfilter);
          deepfilter = nullptr;
          return false;
        }
      } else if (dfs_set_atten_lim(deepfilter, atten) != 0) {
        return false;
      }
    }

    AudioProcessing::Config pre_cfg;
    pre_cfg.pipeline.maximum_internal_processing_rate = MICPROC_SAMPLE_RATE;
    pre_cfg.high_pass_filter.enabled = c.hpf_enabled != 0;
    pre_cfg.echo_canceller.enabled = c.aec_enabled != 0;
    pre_cfg.echo_canceller.mobile_mode = false;
    pre_cfg.noise_suppression.enabled = c.ns_mode == MICPROC_NS_WEBRTC;
    pre_cfg.noise_suppression.level =
        static_cast<AudioProcessing::Config::NoiseSuppression::Level>(c.ns_level);
    pre->ApplyConfig(pre_cfg);

    AudioProcessing::Config post_cfg;
    post_cfg.pipeline.maximum_internal_processing_rate = MICPROC_SAMPLE_RATE;
    post_cfg.gain_controller2.enabled = c.agc_enabled != 0;
    post_cfg.gain_controller2.adaptive_digital.enabled = true;
    post->ApplyConfig(post_cfg);

    if (c.ns_mode != MICPROC_NS_RNNOISE) speech_probability = -1.0f;
    if (c.ns_mode != MICPROC_NS_DEEPFILTER) ns_snr_db = std::numeric_limits<float>::quiet_NaN();
    aec_enabled.store(c.aec_enabled != 0);
    config = c;
    return true;
  }
};

extern "C" {

int32_t micproc_api_version(void) { return MICPROC_API_VERSION; }

void micproc_default_config(micproc_config* config) {
  if (!config) return;
  config->aec_enabled = 1;
  config->ns_mode = MICPROC_NS_RNNOISE;
  config->ns_level = 2;  // High: keeps a faint floor instead of gating word gaps to silence
  config->agc_enabled = 1;
  config->hpf_enabled = 1;
}

micproc* micproc_create(const micproc_config* config) {
  micproc_config c;
  if (config) {
    if (!ValidConfig(*config)) return nullptr;
    c = *config;
  } else {
    micproc_default_config(&c);
  }

  micproc* p = new (std::nothrow) micproc();
  if (!p) return nullptr;
  p->pre = webrtc::AudioProcessingBuilder().Create();
  p->post = webrtc::AudioProcessingBuilder().Create();
  p->rnnoise = rnnoise_create(nullptr);
  if (!p->pre || !p->post || !p->rnnoise || !p->Apply(c)) {
    delete p;
    return nullptr;
  }
  return p;
}

void micproc_destroy(micproc* p) { delete p; }

int32_t micproc_set_config(micproc* p, const micproc_config* config) {
  if (!p || !config) return MICPROC_ERR_NULL;
  if (!ValidConfig(*config)) return MICPROC_ERR_ARG;
  std::lock_guard<std::mutex> lock(p->capture_mutex);
  return p->Apply(*config) ? MICPROC_OK : MICPROC_ERR_INTERNAL;
}

int32_t micproc_get_config(const micproc* p, micproc_config* config) {
  if (!p || !config) return MICPROC_ERR_NULL;
  std::lock_guard<std::mutex> lock(const_cast<micproc*>(p)->capture_mutex);
  *config = p->config;
  return MICPROC_OK;
}

int32_t micproc_process_render(micproc* p, const float* frame, int32_t channels) {
  if (!p || !frame) return MICPROC_ERR_NULL;
  if (channels < 1 || channels > kMaxRenderChannels) return MICPROC_ERR_ARG;
  if (!p->aec_enabled.load()) return MICPROC_OK;

  // APM wants deinterleaved channels; stack buffers keep this allocation-free.
  float planar[kMaxRenderChannels][MICPROC_FRAME_SIZE];
  float* ptrs[kMaxRenderChannels];
  for (int ch = 0; ch < channels; ++ch) {
    ptrs[ch] = planar[ch];
    for (int i = 0; i < MICPROC_FRAME_SIZE; ++i)
      planar[ch][i] = frame[i * channels + ch];
  }
  webrtc::StreamConfig sc(MICPROC_SAMPLE_RATE, channels);
  // Output buffers are required but unused (no render-side processing).
  int err = p->pre->ProcessReverseStream(ptrs, sc, sc, ptrs);
  return err == AudioProcessing::kNoError ? MICPROC_OK : MICPROC_ERR_INTERNAL;
}

int32_t micproc_process_capture(micproc* p, float* frame) {
  if (!p || !frame) return MICPROC_ERR_NULL;
  std::lock_guard<std::mutex> lock(p->capture_mutex);
  const micproc_config& c = p->config;
  webrtc::StreamConfig mono(MICPROC_SAMPLE_RATE, 1);
  float* ch[] = {frame};

  if (c.aec_enabled) p->pre->set_stream_delay_ms(p->stream_delay_ms.load());
  if (p->pre->ProcessStream(ch, mono, mono, ch) != AudioProcessing::kNoError)
    return MICPROC_ERR_INTERNAL;

  if (c.ns_mode == MICPROC_NS_RNNOISE) {
    for (int i = 0; i < MICPROC_FRAME_SIZE; ++i) p->scratch[i] = frame[i] * kRnnoiseScale;
    p->speech_probability = rnnoise_process_frame(p->rnnoise, p->scratch, p->scratch);
    // The slot about to be overwritten holds the input from kRnnoiseDelayFrames ago
    float* dry = p->rnnoise_dry[p->rnnoise_dry_pos];
    const float mix = kRnnoiseDryMix[c.ns_level];
    for (int i = 0; i < MICPROC_FRAME_SIZE; ++i) {
      const float input = frame[i];
      // Scaled so speech, which RNNoise passes, keeps its level
      frame[i] = (1.0f - mix) * (p->scratch[i] / kRnnoiseScale) + mix * dry[i];
      dry[i] = input;
    }
    p->rnnoise_dry_pos = (p->rnnoise_dry_pos + 1) % kRnnoiseDelayFrames;
  } else if (c.ns_mode == MICPROC_NS_DEEPFILTER) {
    // NaN = DeepFilterNet failed; dfshim then passes the frame through unchanged.
    p->ns_snr_db = dfs_process(p->deepfilter, frame, frame);
  }

  if (c.agc_enabled &&
      p->post->ProcessStream(ch, mono, mono, ch) != AudioProcessing::kNoError)
    return MICPROC_ERR_INTERNAL;
  return MICPROC_OK;
}

int32_t micproc_set_stream_delay_ms(micproc* p, int32_t delay_ms) {
  if (!p) return MICPROC_ERR_NULL;
  if (delay_ms < 0 || delay_ms > 500) return MICPROC_ERR_ARG;
  p->stream_delay_ms.store(delay_ms);
  return MICPROC_OK;
}

int32_t micproc_get_stats(micproc* p, micproc_stats* stats) {
  if (!p || !stats) return MICPROC_ERR_NULL;
  webrtc::AudioProcessingStats s = p->pre->GetStatistics();
  stats->aec_delay_ms = s.delay_ms ? *s.delay_ms : -1;
  stats->aec_erle_db = s.echo_return_loss_enhancement
                           ? static_cast<float>(*s.echo_return_loss_enhancement)
                           : std::numeric_limits<float>::quiet_NaN();
  std::lock_guard<std::mutex> lock(p->capture_mutex);
  stats->speech_probability = p->speech_probability;
  stats->ns_snr_db = p->ns_snr_db;
  return MICPROC_OK;
}

}  // extern "C"
