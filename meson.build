project('better-mic', 'c', 'cpp',
  version : '0.1.0',
  meson_version : '>= 1.1',
  default_options : [
    'buildtype=release',
    'default_library=static',
    'warning_level=1',
    'c_std=c11',
    # C++20 everywhere: webrtc-audio-processing uses designated initializers that
    # MSVC rejects in C++17, and abseil must be built with the same standard as
    # everything that includes it.
    'cpp_std=c++20',
    'webrtc-audio-processing:cpp_std=c++20',
    'abseil-cpp:cpp_std=c++20',
    'b_vscrt=static_from_buildtype',
  ])

cc = meson.get_compiler('c')
is_msvc = cc.get_argument_syntax() == 'msvc'

apm_dep = dependency('webrtc-audio-processing-2', fallback : 'webrtc-audio-processing')

# --- RNNoise (built from source; no Meson build upstream) -------------------
rnnoise_model_c = files('third_party/rnnoise-model/src/rnnoise_data.c')
rnnoise_inc = include_directories(
  'native/rnnoise_config', 'third_party/rnnoise/include',
  'third_party/rnnoise/src', 'third_party/rnnoise-model/src')
rnnoise_c_args = ['-DHAVE_CONFIG_H']
if is_msvc
  rnnoise_c_args += ['/wd4244', '/wd4305', '/wd4267', '/D_CRT_SECURE_NO_WARNINGS']
endif

rnnoise_src = files(
  'third_party/rnnoise/src/denoise.c',
  'third_party/rnnoise/src/rnn.c',
  'third_party/rnnoise/src/pitch.c',
  'third_party/rnnoise/src/kiss_fft.c',
  'third_party/rnnoise/src/celt_lpc.c',
  'third_party/rnnoise/src/nnet.c',
  'third_party/rnnoise/src/nnet_default.c',
  'third_party/rnnoise/src/parse_lpcnet_weights.c',
  'third_party/rnnoise/src/rnnoise_tables.c',
  'third_party/rnnoise/src/x86/x86_dnn_map.c',
  'third_party/rnnoise/src/x86/x86cpu.c',
) + rnnoise_model_c

# SIMD kernels need their ISA enabled at compile time; x86cpu.c picks one at runtime.
rnnoise_sse41 = static_library('rnnoise_sse4_1',
  'third_party/rnnoise/src/x86/nnet_sse4_1.c',
  include_directories : rnnoise_inc,
  # MSVC has no SSE4.1 switch on x64 and never defines __SSE4_1__;
  # x86_arch_macros.h defines it from this.
  c_args : rnnoise_c_args + (is_msvc ? ['-DOPUS_X86_MAY_HAVE_SSE4_1'] : ['-msse4.1']))
rnnoise_avx2 = static_library('rnnoise_avx2',
  'third_party/rnnoise/src/x86/nnet_avx2.c',
  include_directories : rnnoise_inc,
  # /arch:AVX2 implies FMA but MSVC doesn't define __FMA__, which vec_avx.h checks.
  c_args : rnnoise_c_args + (is_msvc ? ['/arch:AVX2', '-D__FMA__'] : ['-mavx2', '-mfma']))

rnnoise_lib = static_library('rnnoise', rnnoise_src,
  include_directories : rnnoise_inc,
  c_args : rnnoise_c_args,
  link_whole : [rnnoise_sse41, rnnoise_avx2])
rnnoise_dep = declare_dependency(
  link_with : rnnoise_lib,
  include_directories : include_directories('third_party/rnnoise/include'))

# --- DeepFilterNet3 (Rust staticlib via cargo; model embedded) ----------------
python = import('python').find_installation()
dfshim_ct = custom_target('dfshim',
  output : ['dfshim.lib', 'windows.0.52.0.lib'],
  command : [python, files('scripts/cargo_build.py'),
             meson.current_source_dir() / 'native/dfshim/Cargo.toml',
             meson.current_build_dir() / 'cargo-target', '@OUTDIR@'],
  build_always_stale : true,  # cargo does its own up-to-date check
  console : true)
dfshim_dep = declare_dependency(
  link_with : dfshim_ct[0],
  sources : dfshim_ct[1],  # import lib only has to exist in the build dir
  # From `cargo rustc -- --print native-static-libs`
  link_args : ['bcrypt.lib', 'advapi32.lib', 'ntdll.lib', 'userenv.lib', 'ws2_32.lib',
               'dbghelp.lib', '/LIBPATH:' + meson.current_build_dir()])

# --- micproc.dll -------------------------------------------------------------
micproc_lib = shared_library('micproc', 'native/micproc.cc',
  cpp_args : ['-DMICPROC_BUILD'],
  dependencies : [apm_dep, rnnoise_dep, dfshim_dep],
  gnu_symbol_visibility : 'hidden',
  install : true)
micproc_dep = declare_dependency(
  link_with : micproc_lib,
  include_directories : include_directories('native'))

# --- tests -------------------------------------------------------------------
apm_smoke = executable('apm_smoke', 'tests/apm_smoke.cc', dependencies : apm_dep)
test('apm_smoke', apm_smoke, timeout : 120)

micproc_test = executable('micproc_test', 'tests/micproc_test.c', dependencies : micproc_dep)
test('micproc_test', micproc_test, timeout : 120)
