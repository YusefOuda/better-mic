# Better Mic

A Windows system tray app that cleans up your microphone in real time: noise removal, echo
cancellation and automatic gain. A lightweight alternative to NVIDIA Broadcast's mic feature that
works on any CPU, no NVIDIA GPU needed. Made for gaming and Discord.

It only processes **your own mic**. No camera effects and no processing of incoming audio.

## Features

- **Noise removal** with a choice of engines:
  - **RNNoise** (default): light neural network; handles fans, hum and room noise and sounds natural
  - **DeepFilterNet 3**: stronger; better on keyboard clicks and background voices, adds ~40 ms of latency
  - **WebRTC**: basic noise suppression
  - Strength setting (Low / Medium / High / Maximum) for DeepFilterNet and WebRTC
- **Echo cancellation** (WebRTC AEC3) removes game and voice-chat audio that your mic picks up from
  your speakers. **Auto** mode turns it on only when you're using speakers, not headphones or a headset
- **Automatic gain control** (WebRTC AGC2) evens out your volume, with a limiter
- Picks your real mic automatically and never loops back its own output or another app's virtual mic
- Follows device changes (headset on/off, default device switches) and restarts by itself after errors
- Bypass toggle: sends your mic through unprocessed without changing any app settings
- Light: about 0.3 ms of CPU per 10 ms of audio on a desktop CPU
- Daily update check against GitHub Releases (can be turned off)
- Start with Windows
- Settings saved in `%APPDATA%\better-mic\settings.json`

## How it works

Windows has no way for an app to create a microphone without a signed kernel driver, so Better Mic
uses the free [VB-Cable](https://vb-audio.com/Cable/) virtual audio driver:

```
Your mic ──► Better Mic (echo ─► noise ─► gain) ──► CABLE Input ══► CABLE Output ──► Discord / games
Speakers (loopback) ──┘ (echo reference)
```

Apps select **CABLE Output** as their microphone and get the cleaned-up audio.

## Requirements

- Windows 10 / 11 (x64)
- [VB-Cable](https://vb-audio.com/Cable/) (free, donationware). The installer checks for it and walks
  you through installing it. Its license doesn't allow other installers to install it for you.

## Installation

1. Download `better-mic-setup.exe` from [Releases](../../releases) and run it. It installs to
   `%LOCALAPPDATA%\better-mic` with no UAC prompt.
2. If VB-Cable isn't installed, the installer's last page links to the download. Extract the zip,
   right-click `VBCABLE_Setup_x64.exe` → **Run as administrator** → **Install Driver**, then restart
   Windows if asked. Better Mic starts working as soon as VB-Cable appears.

**Portable:** download `better-mic.zip`, extract `BetterMic.exe` and `micproc.dll` to a permanent
folder (keep them together), and run `BetterMic.exe`. Turn on **Settings → Start with Windows** from
the tray menu *after* placing the files in their final location.

## Setup

### Discord

**User Settings → Voice & Video:**

- **Input Device:** `CABLE Output (VB-Audio Virtual Cable)`
- Turn **off** Noise Suppression (Krisp), Echo Cancellation and Automatic Gain Control. Better Mic
  already does these; processing your voice twice sounds worse.

### Games and other apps

Pick **CABLE Output** as the microphone in the game's voice chat settings. Or make CABLE Output
the Windows default recording device, so every app uses it. Better Mic keeps capturing your real
mic either way.

### Hearing yourself

To check how you sound: **Sound settings → More sound settings → Recording → CABLE Output →
Properties → Listen → "Listen to this device"**. Turn it off again when you're done.

### Keep VB-Cable's playback device enabled

VB-Cable adds a playback device called **Speakers (VB-Audio Virtual Cable)** (CABLE Input). Better
Mic sends your processed voice there, so it must stay **enabled**; just don't make it your default
output. The extra **CABLE In 16 Ch** device isn't needed and can be disabled.

## Usage

Right-click the tray icon:

- **Status** (top line): Active, Bypassed, or what's wrong (for example "VB-Cable is not installed")
- **Mic**: the microphone being processed
- **Process mic**: uncheck to bypass. Your mic still reaches CABLE Output, unprocessed
- **Noise removal**: engine (RNNoise / DeepFilterNet / WebRTC / Off) and **Strength**
- **Echo cancellation**: **Auto** (on for speakers, off for headphones/headsets, based on what
  Windows reports for your default output), **Always on**, or **Off**
- **Automatic gain**
- **Microphone**: **Automatic** (your Windows default mic, skipping VB-Cable and other virtual mics)
  or a specific device
- **Discord setup…**: the setup steps above
- **Settings**: Start with Windows, Check for updates

The tray icon is green while processing, grey with a slash when bypassed, and red when something
needs attention.

## Troubleshooting

- **"VB-Cable is not installed"**: install it (see Installation). If you just installed it, restart Windows.
- **"VB-Cable's playback device is disabled"**: enable **Speakers (VB-Audio Virtual Cable)** under
  Sound settings → More sound settings → Playback (right-click → Enable).
- **Others hear nothing**: check that the app (Discord, the game) uses **CABLE Output** as its input,
  and that the **Mic** line in the tray menu shows your real microphone.
- **Voice sounds choppy or cut off with DeepFilterNet**: lower **Strength** to Medium or High, or use RNNoise.
- **Diagnostics**: `BetterMic.exe --selftest 10 > selftest.txt` runs the audio pipeline for 10
  seconds without the tray UI and writes the chosen devices and health counters (frames, processing
  time, underruns, drops) to `selftest.txt`. Nothing is recorded.

## Building

Requirements: Visual Studio 2022 Build Tools (C++ workload), [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0),
Python 3 with `pip install meson ninja`, and [Rust](https://rustup.rs/) (stable, MSVC).

```powershell
git clone --recurse-submodules https://github.com/YusefOuda/better-mic.git
cd better-mic
scripts\build.cmd          # builds build\micproc.dll (WebRTC APM + RNNoise + DeepFilterNet) and runs native tests
dotnet test better-mic.sln
dotnet run --project app\BetterMic.csproj
```

Keep the checkout path short (for example `C:\code\better-mic`): some WebRTC build paths are long,
and MSVC fails past Windows' 260-character path limit.

Release builds are made by [`.github/workflows/release.yml`](.github/workflows/release.yml) when a
`v*` tag is pushed. Local builds are version 0.0.0 and skip the update check.

| Path | Contents |
|---|---|
| `native/` | `micproc` C API (P/Invoke target) and the DeepFilterNet Rust shim |
| `app/` | C# tray app (WASAPI capture, VB-Cable output, tray menu) and its tests |
| `subprojects/webrtc-audio-processing` | WebRTC audio processing (submodule) |
| `third_party/` | RNNoise and DeepFilterNet (submodules) |
| `tests/` | Native tests |

## Third-party software

Better Mic uses [WebRTC Audio Processing](https://gitlab.freedesktop.org/pulseaudio/webrtc-audio-processing)
(BSD-3-Clause), [Abseil](https://github.com/abseil/abseil-cpp) (Apache-2.0),
[RNNoise](https://github.com/xiph/rnnoise) (BSD-3-Clause), [DeepFilterNet](https://github.com/Rikorose/DeepFilterNet)
(MIT / Apache-2.0) and [NAudio](https://github.com/naudio/NAudio) (MIT). Full notices are in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

[VB-Cable](https://vb-audio.com/Cable/) is a separate donationware product by VB-Audio and isn't
included.
