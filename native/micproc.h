/*
 * micproc: real-time microphone cleanup (echo cancellation, noise removal,
 * automatic gain control). Plain C API intended for P/Invoke.
 *
 * Audio format: 48 kHz, 32-bit float in [-1, 1], 10 ms frames (480 samples
 * per channel, MICPROC_FRAME_SIZE). Capture (mic) is mono. Render (speaker
 * loopback reference) may have 1..8 channels, interleaved.
 *
 * Pipeline per capture frame:
 *   high-pass -> AEC3 -> noise suppression -> AGC2 (+ limiter)
 *
 * Latency added by noise suppression: DeepFilterNet ~40 ms (lookahead),
 * RNNoise ~10 ms, WebRTC NS ~0.
 *
 * Threading: micproc_process_render() may run on a different thread than
 * micproc_process_capture() (typical: loopback thread vs. mic thread). All
 * other calls may come from any thread; capture processing and config
 * changes are serialized internally. A single instance must not have two
 * concurrent callers of the same process function.
 *
 * Return values: 0 on success, negative MICPROC_ERR_* on failure.
 */
#ifndef MICPROC_H_
#define MICPROC_H_

#include <stdint.h>

#ifdef _WIN32
#  ifdef MICPROC_BUILD
#    define MICPROC_API __declspec(dllexport)
#  else
#    define MICPROC_API __declspec(dllimport)
#  endif
#  define MICPROC_CALL __cdecl
#else
#  define MICPROC_API __attribute__((visibility("default")))
#  define MICPROC_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define MICPROC_API_VERSION 1
#define MICPROC_SAMPLE_RATE 48000
#define MICPROC_FRAME_SIZE 480 /* samples per channel per 10 ms frame */

enum {
  MICPROC_OK = 0,
  MICPROC_ERR_NULL = -1,       /* null handle or buffer */
  MICPROC_ERR_ARG = -2,        /* invalid argument (channels, enum value, ...) */
  MICPROC_ERR_INTERNAL = -3,   /* APM or allocation failure */
};

typedef enum micproc_ns_mode {
  MICPROC_NS_OFF = 0,
  MICPROC_NS_WEBRTC = 1,  /* WebRTC APM noise suppression, see ns_level */
  MICPROC_NS_RNNOISE = 2, /* RNNoise: light, good on steady noise (fans, hum) */
  MICPROC_NS_DEEPFILTER = 3, /* DeepFilterNet3: best quality, incl. keyboards and voices */
} micproc_ns_mode;

/* Field order and sizes are part of the ABI: all int32_t, no padding. */
typedef struct micproc_config {
  int32_t aec_enabled;   /* echo cancellation; needs render frames */
  int32_t ns_mode;       /* micproc_ns_mode */
  int32_t ns_level;      /* strength 0 low .. 3 max; WebRTC and DeepFilter (ignored by RNNoise) */
  int32_t agc_enabled;   /* adaptive digital gain + limiter */
  int32_t hpf_enabled;   /* high-pass filter (removes DC and rumble) */
} micproc_config;

typedef struct micproc_stats {
  float speech_probability; /* last frame, 0..1; RNNoise mode only, else -1 */
  int32_t aec_delay_ms;     /* AEC's estimated render->capture delay, -1 if unknown */
  float aec_erle_db;        /* echo return loss enhancement, NaN if unknown */
  float ns_snr_db;          /* DeepFilter's local SNR estimate, last frame; NaN otherwise */
} micproc_stats;

typedef struct micproc micproc;

MICPROC_API int32_t MICPROC_CALL micproc_api_version(void);

/* Fills *config with defaults: AEC on, DeepFilterNet at level 3, AGC on, HPF on. */
MICPROC_API void MICPROC_CALL micproc_default_config(micproc_config* config);

/* Returns NULL on failure. config may be NULL for defaults. */
MICPROC_API micproc* MICPROC_CALL micproc_create(const micproc_config* config);
MICPROC_API void MICPROC_CALL micproc_destroy(micproc* p);

/* Applies a new configuration. Takes effect from the next capture frame.
 * Switching to DeepFilter the first time loads the model (~100 ms; blocks capture). */
MICPROC_API int32_t MICPROC_CALL micproc_set_config(micproc* p, const micproc_config* config);
MICPROC_API int32_t MICPROC_CALL micproc_get_config(const micproc* p, micproc_config* config);

/* Feeds one 10 ms frame of what the speakers are playing (interleaved,
 * MICPROC_FRAME_SIZE * channels samples). Not modified. Cheap no-op when AEC
 * is disabled. */
MICPROC_API int32_t MICPROC_CALL micproc_process_render(micproc* p, const float* frame,
                                                        int32_t channels);

/* Processes one 10 ms mono mic frame (MICPROC_FRAME_SIZE samples) in place. */
MICPROC_API int32_t MICPROC_CALL micproc_process_capture(micproc* p, float* frame);

/* Hint for AEC: approximate delay between a render frame being passed to
 * micproc_process_render() and its echo arriving in micproc_process_capture()
 * (output latency + input latency). AEC3 refines this itself. */
MICPROC_API int32_t MICPROC_CALL micproc_set_stream_delay_ms(micproc* p, int32_t delay_ms);

MICPROC_API int32_t MICPROC_CALL micproc_get_stats(micproc* p, micproc_stats* stats);

#ifdef __cplusplus
}
#endif

#endif /* MICPROC_H_ */
