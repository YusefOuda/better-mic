/* Hand-written replacement for RNNoise's autotools config.h (x86-64, MSVC or GCC/Clang). */
#ifndef RNNOISE_CONFIG_H_
#define RNNOISE_CONFIG_H_

/* Runtime CPU detection: picks the SSE4.1 / AVX2 kernels when available.
 * Don't define OPUS_X86_MAY_HAVE_* here: celt_lpc.h then includes an Opus
 * header RNNoise doesn't ship. meson.build sets it for nnet_sse4_1.c only. */
#define RNN_ENABLE_X86_RTCD 1
#define CPU_INFO_BY_ASM 1 /* ignored by MSVC, which uses __cpuid */

#endif
