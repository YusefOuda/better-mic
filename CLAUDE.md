# Mic Voice Processor — Project Context

Handoff from a claude.ai planning chat.

## Status / build
Steps 1 and 2 done. `scripts\build.cmd` (MSVC x64 Release) fetches the RNNoise model, builds
everything with Meson and runs the tests. Output: `build\micproc.dll`.

Layout:
- `native/micproc.h` / `micproc.cc`: the C API (P/Invoke target). 48 kHz float, 480-sample frames,
  mono capture, 1..8 ch interleaved render. Two APM instances: pre (HPF, AEC3, WebRTC NS) and
  post (AGC2), with RNNoise between them. NS mode selectable: off / WebRTC / RNNoise (default).
- `subprojects/webrtc-audio-processing`: submodule, v2.1 (Meson subproject; pulls abseil via wrap).
- `third_party/rnnoise`: submodule, master @ 70f1d25 (newer model than v0.2), compiled directly
  from `meson.build`. Model weights (`rnnoise_data.c`, ~78 MB source) are downloaded and
  SHA-256 checked by `scripts\fetch-rnnoise-model.ps1` into `third_party/rnnoise-model` (gitignored).
- `tests/apm_smoke.cc` (raw APM) and `tests/micproc_test.c` (DLL API, AEC, RNNoise, cost).

Gotchas:
- Everything is **C++20**: upstream APM uses designated initializers MSVC rejects in C++17, and
  abseil must match the std of its users.
- Static CRT (`b_vscrt=static_from_buildtype`): DLL depends only on KERNEL32 + WINMM.
- WebRTC hard-codes `WEBRTC_ENABLE_SYMBOL_EXPORT`, so ~156 WebRTC C++ symbols are also exported
  from micproc.dll. Harmless; can't be undone from outside (its `/D` comes after ours).
- Measuring AEC: keep NS off, or NS masks it. Pure tones are a bad AEC test signal.
- RNNoise VAD (`speech_probability`) is meaningless on synthetic signals; judge it on real speech.
- AEC stats look off on synthetic input (ERLE ~0.2 dB, delay 16 ms vs real 40 ms) although
  cancellation works (~20 dB). Recheck with real audio before showing them in a UI.
- Measured cost: ~0.34 ms per 10 ms frame for AEC + RNNoise + AGC (3.4% of one core).
- Keep the checkout path short (well under ~100 chars): some WebRTC object paths in `build\` are
  ~150 chars, and MSVC fails past MAX_PATH (260) with "Cannot open compiler generated file".
- Tooling: Python 3.12 (user install) + `pip install --user meson ninja`.
- Fresh checkout: `git clone --recurse-submodules`, then `scripts\build.cmd`.

## Goal
A Windows app that cleans up the microphone signal in real time, as a lightweight
alternative to NVIDIA Broadcast's mic feature. Should work on any GPU/CPU (no NVIDIA dependency).

Features wanted:
1. Noise removal (required)
2. Echo cancellation (required)
3. Automatic gain control (nice to have)

Out of scope: camera effects, eye contact, auto-framing.

## Environment
- Windows 11 24H2 gaming/streaming PC
- Headset: SteelSeries Arctis Nova 3X Wireless (USB dongle). Falls back to speakers when the headset is off.
- Related project: `usb-headset-tray` (C# Windows tray app using HeadsetControl, handles auto-switching output).
  C# is the likely language here too, for consistency (not yet decided).

## Proposed approach (not final)

### Core library: WebRTC Audio Processing Module (APM)
- Use the standalone `webrtc-audio-processing` library (maintained by the PulseAudio project, builds with Meson).
- Provides AEC3 (echo cancellation), noise suppression, and AGC2 (gain control + limiter).
- Wrap in a small C DLL with a minimal API and call it from C# via P/Invoke.

### Optional better noise removal
- RNNoise: very light, CPU-only, good on fans and hum.
- DeepFilterNet: noticeably better on keyboards and background voices. ONNX model, real-time on CPU.
  Could run via ONNX Runtime (DirectML provider if GPU is wanted).
- If an ML model is used, disable APM's built-in NS.

### Pipeline
```
Mic (WASAPI, 48 kHz mono, 10 ms frames) ─┐
                                         ├─► AEC3 ─► Noise removal ─► AGC2 ─► Virtual mic output
Speaker loopback (WASAPI loopback) ──────┘
```
- Order matters: echo → noise → gain. (Gain before noise removal amplifies the noise.)
- To keep that order with an ML denoiser in the middle: two APM instances (one AEC-only, one AGC-only).
- AEC3 needs the render reference (what the speakers are playing) via WASAPI loopback on the
  default render device. It estimates the delay itself; still set a sensible stream delay hint.

### Echo cancellation and the headset
- With the headset on, echo is basically a non-issue.
- Idea: enable AEC only when the default output is the speakers (could integrate with
  usb-headset-tray's switching logic). Saves CPU and avoids AEC degrading speech unnecessarily.

### Virtual microphone output
- Windows has no user-mode API for creating a virtual mic; a real device needs a signed kernel
  driver (EV cert + Microsoft attestation). Test signing/test mode is a bad idea on a gaming PC (anti-cheat).
- Plan: write processed audio to VB-Audio Virtual Cable; apps select "CABLE Output" as their mic.
- Alternative: package the processing as a VST plugin and host it in OBS or Equalizer APO.

## Suggested next steps
1. Build `webrtc-audio-processing` on Windows (Meson) and confirm it links.
2. Design the C wrapper API (create/destroy, configure AEC/NS/AGC, process capture frame,
   process render frame, set stream delay).
3. C# capture loop: WASAPI mic capture + loopback capture, 10 ms framing, resampling to 48 kHz if needed,
   output to VB-Cable.
4. Get AEC + AGC working end to end, then evaluate RNNoise vs DeepFilterNet for the NS stage.
5. Later: tray UI (toggles per feature, device pickers), AEC auto-enable based on output device.

## Open decisions
- Language confirmation (C# assumed)
- Which noise suppressor: WebRTC NS, RNNoise, or DeepFilterNet (RNNoise is the current default;
  needs evaluation on real recordings, and DeepFilterNet would be a new `micproc_ns_mode`)
- Standalone app vs. VST plugin
- Whether to merge into usb-headset-tray or keep separate