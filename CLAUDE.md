# Mic Voice Processor — Project Context

Handoff from a claude.ai planning chat.

## Status / build
- Step 1 done: `webrtc-audio-processing` v2.1 (git submodule in `third_party/`) builds as a static
  x64 Release lib with MSVC (VS 2022 Build Tools) via `scripts\build-webrtc-apm.cmd`.
  Output: `build\webrtc-apm-install` (headers + lib); abseil static libs stay in
  `build\webrtc-apm\subprojects\abseil-cpp-*`.
- Must build as **C++20** (`-Dcpp_std=c++20 -Dabseil-cpp:cpp_std=c++20`): upstream uses designated
  initializers that MSVC rejects in C++17. Anything linking abseil/APM must also use C++20.
- `scripts\build-apm-smoke.cmd` builds/runs `tests\apm_smoke.cc`: AEC on vs off on synthetic echo
  (~20 dB improvement). Keep NS off when measuring AEC, or NS masks it.
- Tooling: Python 3.12 (user install) + `pip install --user meson ninja`.

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
- Which noise suppressor: WebRTC NS, RNNoise, or DeepFilterNet
- Standalone app vs. VST plugin
- Whether to merge into usb-headset-tray or keep separate