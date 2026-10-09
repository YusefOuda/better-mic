# Mic Voice Processor — Project Context

Started as a handoff from a claude.ai planning chat.

## Goal and scope
A Windows tray app that cleans up **the user's own microphone** in real time, mainly for gaming
and Discord. Much smaller in scope than NVIDIA Broadcast: only the mic path. No camera effects,
no speaker/incoming-audio denoising. Must work on any GPU/CPU (no NVIDIA dependency).

Features:
1. Noise removal (required). Priority is the best quality available.
2. Echo cancellation (required)
3. Automatic gain control (nice to have)

Distribution: all-in-one, with an Inno Setup installer like the user's other tray apps
(`C:\code\usb-headset-tray`, `C:\code\hdr-tray`: .NET 8 WinForms `ApplicationContext` +
`NotifyIcon`, single-file publish, `installer.iss`, GitHub release workflow).

## Decisions
- **Language:** C# (.NET 8 WinForms tray app) for the app; native DSP in `micproc.dll` via P/Invoke.
- **Separate app**, not merged into usb-headset-tray and not a VST plugin.
- **Noise suppressor:** RNNoise is the default: in the user's own listening test (Oct 2026, real
  mic, Arctis headset) it sounded best. DeepFilterNet3 stays available as the stronger option for
  keyboards and background voices (on paper the best open model on CPU; ~40 ms added latency).
  It was tested at Maximum strength (unlimited attenuation, gates to silence between words), so
  Medium/High may compare better; worth re-testing. WebRTC NS also available.
- **Virtual mic:** VB-Cable. Its license (readme in the driver pack) forbids integrating it "in
  another software installation procedure without Author agreement", so we do NOT download or run
  its setup ourselves. The installer checks for it (`BetterMic.exe --check-cable`) and, if missing,
  guides the user to vb-audio.com and re-checks; the tray app also starts on its own once the
  device appears. If VB-Audio ever grants permission: `VBCABLE_Setup_x64.exe -i -h`, elevated,
  then reboot. Apps pick "CABLE Output" as their mic.
  Rejected: our own attestation-signed driver (EV cert, kernel code), an APO on the real mic
  (needs `DisableProtectedAudioDG`, fragile), test signing (anti-cheat), and
  VirtualDrivers/Virtual-Audio-Driver (beta, test-signed only).
- **Discord:** users should turn off Discord's own noise suppression (Krisp), echo cancellation
  and AGC when using our mic; double processing sounds worse. Say so in the app and README.

## Build notes
- `native/dfshim/Cargo.lock` was seeded from DeepFilterNet's own lockfile (known-good versions);
  the build uses `--locked`.
- RNNoise is pinned to master @ 70f1d25, not v0.2: newer model, and v0.2's `model_version` is a
  short hash that can't be checksum-verified.

Gotchas:
- Everything is **C++20**: upstream APM uses designated initializers MSVC rejects in C++17, and
  abseil must match the std of its users.
- Static CRT everywhere (`b_vscrt=static_from_buildtype`, Rust `+crt-static`): DLL depends only on
  KERNEL32 + WINMM (+ system DLLs the Rust std needs).
- WebRTC hard-codes `WEBRTC_ENABLE_SYMBOL_EXPORT`, so ~156 WebRTC C++ symbols are also exported
  from micproc.dll. Harmless; can't be undone from outside (its `/D` comes after ours).
- Measuring AEC: keep NS off, or NS masks it. Pure tones are a bad AEC test signal.
- RNNoise VAD (`speech_probability`) is meaningless on synthetic signals; judge it on real speech.
- DeepFilterNet outputs silence and skips its network for frames below -10 dB local SNR (pure
  noise) and skips processing above 30 dB. Noise-only input therefore understates its CPU cost;
  `micproc_stats.ns_snr_db` shows which band a frame fell in.
- AEC stats look off on synthetic input (ERLE ~0.2 dB, delay 16 ms vs real 40 ms) although
  cancellation works (~20 dB). Recheck with real audio before showing them in a UI.
- Measured cost (this PC): full pipeline ~0.3-0.4 ms per 10 ms frame with either RNNoise or
  DeepFilterNet; DeepFilterNet full-path frames ~0.36 ms mean, ~1 ms max.
- Keep the checkout path short (well under ~100 chars): some WebRTC object paths in `build\` are
  ~150 chars, and MSVC fails past MAX_PATH (260) with "Cannot open compiler generated file".
- Tooling: Python 3.12 (user install) + `pip install --user meson ninja`; Rust stable via rustup
  (`x86_64-pc-windows-msvc`).
- Fresh checkout: `git clone --recurse-submodules`, then `scripts\build.cmd`.

## Environment
- Windows 11 24H2 gaming/streaming PC. VB-Cable 4.5 is installed (render endpoints
  "Speakers (VB-Audio Virtual Cable)" and "CABLE In 16 Ch", capture "CABLE Output").
  NVIDIA Broadcast is installed too (its own virtual mic/speakers).
- Headset: SteelSeries Arctis Nova 3X Wireless (USB dongle). Falls back to speakers when the headset is off.
- Related project: `usb-headset-tray` (handles auto-switching output with HeadsetControl).

## Pipeline
```
Mic (WASAPI, 48 kHz mono, 10 ms frames) ─┐
                                         ├─► AEC3 ─► Noise removal ─► AGC2 ─► VB-Cable ("CABLE Input")
Speaker loopback (WASAPI loopback) ──────┘
```
- Order matters: echo → noise → gain. (Gain before noise removal amplifies the noise.)
- AEC3 needs the render reference (what the speakers are playing) via WASAPI loopback on the
  default render device. It estimates the delay itself; still set a sensible stream delay hint.

## Next steps / working notes
- Open: evaluate on real recordings (keyboard, fans, game audio on speakers); tune NS defaults.
- The user's dev instance often runs from `app\bin\x64\Debug` and locks the exe: build and test
  with `--artifacts-path <scratch dir>` then, and ask before rebuilding into `app\bin`.
- Local installer build: publish to `publish\`, then
  `"%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" /DAppVersion=x.y.z installer.iss`.
- AGC stays WebRTC's default (peaks ~-5 dBFS, boost only). Tried and dropped (user's call):
  Quiet/Normal/Loud AGC levels (felt alike with a hot mic, since AGC2 only boosts) and a fixed
  -15 dB pre-attenuation to make AGC two-way (it pushed normal speech below AGC2's level-dependent
  VAD, so AGC stopped adapting). If revisited: an adaptive downward-only leveler before AGC2 that
  engages only for hot mics.
- Ideas not done: headset-off fallback mic (Arctis dongle stays enumerated when headset is off),
  one-click updates, voice EQ/compressor presets, smarter AEC Auto via HeadsetControl.
