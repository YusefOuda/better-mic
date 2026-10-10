using System.Diagnostics;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using BetterMic.Audio;
using BetterMic.Native;

namespace BetterMic;

class TrayApp : ApplicationContext
{
    const string AppName = "Better Mic";
    const string VbCableUrl = "https://vb-audio.com/Cable/";
    static readonly TimeSpan DeviceSettleDelay = TimeSpan.FromMilliseconds(500);
    static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
    static readonly TimeSpan FirstUpdateCheckDelay = TimeSpan.FromSeconds(30);
    static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromDays(1);

    readonly NotifyIcon _tray;
    readonly AppSettings _settings;
    readonly Devices _devices;
    readonly AudioEngine _engine = new();
    // Device events arrive in bursts (e.g. headset power-on); reconcile once they settle
    readonly System.Windows.Forms.Timer _reconcileTimer = new();
    readonly System.Windows.Forms.Timer _updateTimer = new();
    readonly System.Windows.Forms.Timer _clipTimer = new() { Interval = 1000 };
    readonly ClipWarner _clipWarner = new();
    MicTestForm? _testForm;

    // What the engine is currently running with
    string? _micId;
    string? _cableId;
    string? _loopbackId;
    bool _aecActive;
    FormFactor? _outputFormFactor;
    string? _micName;
    // Why the engine isn't running, or the last audio fault; null when healthy
    string? _problem;
    CableState _cableState = CableState.Ready;
    CableState? _notifiedCableState;
    // Not saved: starting muted after a restart would be easy to miss
    bool _muted;
    string? _availableUpdate;
    Action? _balloonClick;

    public TrayApp()
    {
        _settings = AppSettings.Load();
        _engine.ProcessingEnabled = _settings.ProcessingEnabled;

        _tray = new NotifyIcon { Visible = true, ContextMenuStrip = new ContextMenuStrip() };
        _tray.ContextMenuStrip.Opening += (_, e) =>
        {
            BuildMenu();
            // WinForms pre-cancels Opening when the strip is empty, which it is on the first right-click
            e.Cancel = false;
        };
        _tray.BalloonTipClicked += (_, _) => _balloonClick?.Invoke();
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ToggleMute();
        };

        var ui = SynchronizationContext.Current!;
        _engine.Faulted += message => ui.Post(_ => OnFault(message), null);
        _devices = new Devices();
        _devices.Changed += change =>
        {
            Log.Info($"Device change: {change}");
            ScheduleReconcile(DeviceSettleDelay);
        };

        _reconcileTimer.Tick += (_, _) =>
        {
            _reconcileTimer.Stop();
            Reconcile();
        };

        _updateTimer.Interval = (int)FirstUpdateCheckDelay.TotalMilliseconds;
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = (int)UpdateCheckInterval.TotalMilliseconds;
            if (_settings.CheckForUpdates) await CheckForUpdate();
        };
        _updateTimer.Start();

        _clipTimer.Tick += (_, _) => CheckClipping();
        _clipTimer.Start();

        Reconcile();
        if (!_settings.SetupShown && _cableId != null)
        {
            _settings.SetupShown = true;
            _settings.Save();
            ShowBalloon(AppName, "Running. In Discord, pick \"CABLE Output\" as your input device. Click for setup tips.",
                ToolTipIcon.Info, ShowSetupHelp);
        }
    }

    // --- Engine state ---------------------------------------------------------------

    void ScheduleReconcile(TimeSpan delay)
    {
        _reconcileTimer.Stop();
        _reconcileTimer.Interval = (int)delay.TotalMilliseconds;
        _reconcileTimer.Start();
    }

    // Brings the engine in line with the current devices and settings. Safe to call any time.
    void Reconcile()
    {
        DeviceSelection selection;
        try
        {
            selection = DeviceSelection.Resolve(_devices, _settings);
        }
        catch (Exception e)
        {
            // Endpoints can vanish mid-enumeration while another app (e.g. a streaming host) swaps devices
            Log.Error("Reading audio devices failed; retrying", e);
            ScheduleReconcile(RetryDelay);
            return;
        }
        var (micId, cableId, outputId, aec) = (selection.MicId, selection.CableId, selection.OutputId, selection.AecEnabled);
        _micName = selection.MicName;
        _outputFormFactor = selection.OutputFormFactor;

        if (cableId == null)
        {
            // Starts automatically once VB-Cable is installed or enabled (device notifications)
            _cableState = _devices.GetCableState();
            bool disabled = _cableState == CableState.Disabled;
            StopEngine(disabled ? "VB-Cable's playback device is disabled" : "VB-Cable is not installed");
            if (_notifiedCableState != _cableState)
            {
                _notifiedCableState = _cableState;
                if (disabled)
                    ShowBalloon(AppName, "Enable \"Speakers (VB-Audio Virtual Cable)\" (CABLE Input) under Playback devices. Click to open Sound settings.",
                        ToolTipIcon.Warning, OpenSoundSettings);
                else
                    ShowBalloon(AppName, "Better Mic needs VB-Cable, a free virtual audio driver. Click to download it from vb-audio.com.",
                        ToolTipIcon.Warning, () => OpenUrl(VbCableUrl));
            }
            UpdateTray();
            return;
        }
        _cableState = CableState.Ready;
        _notifiedCableState = null;
        if (micId == null)
        {
            StopEngine("No microphone found");
            UpdateTray();
            return;
        }

        try
        {
            var config = _settings.ToConfig(aec);
            if (!_engine.IsRunning || micId != _micId || cableId != _cableId)
            {
                Log.Info($"Starting mic \"{selection.MicName}\" ({micId}) -> cable {cableId}");
                _engine.Start(micId, cableId, config);
                _micId = micId;
                _cableId = cableId;
            }
            else
            {
                _engine.Configure(config);
            }
            string? loopbackId = aec ? outputId : null;
            if (loopbackId != _loopbackId || (loopbackId != null && !_engine.IsLoopbackRunning))
            {
                Log.Info(loopbackId == null ? "Echo cancellation off"
                    : $"Echo reference: \"{selection.OutputName}\" ({loopbackId}, {selection.OutputFormFactor})");
                _engine.SetLoopback(loopbackId);
                _loopbackId = loopbackId;
            }
            _aecActive = aec;
            _problem = null;
        }
        catch (DllNotFoundException e)
        {
            Log.Error("micproc.dll not found", e);
            StopEngine("micproc.dll is missing; reinstall Better Mic");
        }
        catch (Exception e)
        {
            Log.Error("Starting audio failed; retrying", e);
            StopEngine($"Couldn't start audio: {e.Message}");
            ScheduleReconcile(RetryDelay);
        }
        UpdateTray();
    }

    void StopEngine(string problem)
    {
        if (problem != _problem) Log.Info($"Stopped: {problem}");
        _engine.Stop();
        _micId = _cableId = _loopbackId = null;
        _aecActive = false;
        _problem = problem;
    }

    // Audio thread died (device unplugged, driver reset, ...). Restart from scratch shortly.
    void OnFault(string message)
    {
        StopEngine(message);
        UpdateTray();
        ScheduleReconcile(RetryDelay);
    }

    void ToggleMute()
    {
        _muted = !_muted;
        _engine.Muted = _muted;
        UpdateTray();
    }

    void CheckClipping()
    {
        if (_engine.Counters is not { } counters || _muted) return;
        if (!_clipWarner.Update(counters.ClippedFrames, DateTime.UtcNow)) return;
        ShowBalloon(AppName, "Your mic is clipping (too loud before processing, which no setting can repair). " +
            "Click to open Recording devices and lower your mic's level.", ToolTipIcon.Warning, OpenRecordingSettings);
    }

    void ShowTestWindow()
    {
        if (_testForm is { IsDisposed: false })
        {
            _testForm.Activate();
            return;
        }
        _testForm = new MicTestForm(_engine, () => _problem);
        _testForm.FormClosed += (_, _) => _testForm = null;
        _testForm.Show();
    }

    // --- Tray ------------------------------------------------------------------------

    void UpdateTray()
    {
        var state = _problem != null ? IconState.Problem
            : _muted ? IconState.Muted
            : _settings.ProcessingEnabled ? IconState.Active : IconState.Bypassed;
        var old = _tray.Icon;
        _tray.Icon = IconRenderer.Render(state, SystemInformation.SmallIconSize.Width);
        old?.Dispose();

        string text = _problem ?? (_muted ? "Muted (click to unmute)" : _settings.ProcessingEnabled
            ? $"{NoiseName(_settings.NoiseMode)}, echo {(_aecActive ? "on" : "off")}"
            : "Bypassed (unprocessed mic)");
        // NotifyIcon.Text is limited to 127 characters
        string tip = $"{AppName}: {text}";
        _tray.Text = tip.Length > 127 ? tip[..127] : tip;
    }

    void BuildMenu()
    {
        var menu = _tray.ContextMenuStrip!;
        menu.Items.Clear();

        string status = _problem ?? (_muted ? "Muted" : _settings.ProcessingEnabled ? "Active" : "Bypassed");
        menu.Items.Add(new ToolStripMenuItem($"{AppName}: {status}") { Enabled = false });
        if (_micName != null && _problem == null)
            menu.Items.Add(new ToolStripMenuItem($"Mic: {_micName}") { Enabled = false });
        if (_problem == null && _engine.Counters is { Frames: > 0 } counters)
            menu.Items.Add(new ToolStripMenuItem(
                $"Load: {counters.CpuPercent:F1}% of a CPU core · dropouts: {counters.Dropouts}") { Enabled = false });
        if (_cableState == CableState.Missing)
            menu.Items.Add("Download VB-Cable…", null, (_, _) => OpenUrl(VbCableUrl));
        else if (_cableState == CableState.Disabled)
            menu.Items.Add("Open Sound settings…", null, (_, _) => OpenSoundSettings());
        if (_availableUpdate != null)
            menu.Items.Add($"Update available ({_availableUpdate})", null, (_, _) => OpenUrl(UpdateChecker.ReleasesPage));
        menu.Items.Add(new ToolStripSeparator());

        var mute = new ToolStripMenuItem("Mute (left-click the icon)") { Checked = _muted };
        mute.Click += (_, _) => ToggleMute();
        menu.Items.Add(mute);

        var enabled = new ToolStripMenuItem("Process mic") { Checked = _settings.ProcessingEnabled };
        enabled.Click += (_, _) =>
        {
            _settings.ProcessingEnabled = !_settings.ProcessingEnabled;
            _engine.ProcessingEnabled = _settings.ProcessingEnabled;
            SaveAndApply();
        };
        menu.Items.Add(enabled);

        menu.Items.Add(NoiseMenu());
        menu.Items.Add(EchoMenu());

        var agc = new ToolStripMenuItem("Automatic gain") { Checked = _settings.AutoGain };
        agc.Click += (_, _) =>
        {
            _settings.AutoGain = !_settings.AutoGain;
            SaveAndApply();
        };
        menu.Items.Add(agc);
        menu.Items.Add(MicMenu());

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Test microphone…", null, (_, _) => ShowTestWindow());
        menu.Items.Add("Discord setup…", null, (_, _) => ShowSetupHelp());
        menu.Items.Add(SettingsMenu());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
    }

    ToolStripMenuItem NoiseMenu()
    {
        var root = new ToolStripMenuItem("Noise removal");
        foreach (var mode in new[] { NoiseMode.RNNoise, NoiseMode.DeepFilter, NoiseMode.WebRtc, NoiseMode.Off })
        {
            var item = new ToolStripMenuItem(NoiseName(mode, detailed: true)) { Checked = _settings.NoiseMode == mode };
            item.Click += (_, _) =>
            {
                _settings.NoiseMode = mode;
                SaveAndApply();
            };
            root.DropDownItems.Add(item);
        }
        root.DropDownItems.Add(new ToolStripSeparator());
        bool hasStrength = _settings.NoiseMode != NoiseMode.Off;
        string[] levels = ["Low", "Medium", "High", "Maximum"];
        var strength = new ToolStripMenuItem("Strength") { Enabled = hasStrength };
        for (int level = 0; level < levels.Length; level++)
        {
            int l = level;
            var item = new ToolStripMenuItem(levels[l]) { Checked = _settings.NoiseLevel == l };
            item.Click += (_, _) =>
            {
                _settings.NoiseLevel = l;
                SaveAndApply();
            };
            strength.DropDownItems.Add(item);
        }
        root.DropDownItems.Add(strength);
        return root;
    }

    ToolStripMenuItem EchoMenu()
    {
        var root = new ToolStripMenuItem("Echo cancellation");
        string autoDetail = _outputFormFactor is FormFactor.Headphones or FormFactor.Headset or FormFactor.Handset
            ? "off: headphones" : "on: speakers";
        (AecMode Mode, string Label)[] options =
        [
            (AecMode.Auto, $"Auto, speakers only ({autoDetail})"),
            (AecMode.On, "Always on"),
            (AecMode.Off, "Off"),
        ];
        foreach (var (mode, label) in options)
        {
            var item = new ToolStripMenuItem(label) { Checked = _settings.EchoCancellation == mode };
            item.Click += (_, _) =>
            {
                _settings.EchoCancellation = mode;
                SaveAndApply();
            };
            root.DropDownItems.Add(item);
        }
        return root;
    }

    ToolStripMenuItem MicMenu()
    {
        var root = new ToolStripMenuItem("Microphone");
        var auto = new ToolStripMenuItem("Automatic (Windows default)") { Checked = _settings.MicDeviceId == null };
        auto.Click += (_, _) =>
        {
            _settings.MicDeviceId = null;
            SaveAndApply();
        };
        root.DropDownItems.Add(auto);
        root.DropDownItems.Add(new ToolStripSeparator());
        foreach (var device in _devices.Active(DataFlow.Capture).Where(d => !d.IsVirtualCable))
        {
            var item = new ToolStripMenuItem(device.Name) { Checked = _settings.MicDeviceId == device.Id };
            item.Click += (_, _) =>
            {
                _settings.MicDeviceId = device.Id;
                SaveAndApply();
            };
            root.DropDownItems.Add(item);
        }
        return root;
    }

    ToolStripMenuItem SettingsMenu()
    {
        var root = new ToolStripMenuItem("Settings");
        var startup = new ToolStripMenuItem("Start with Windows") { Checked = _settings.StartWithWindows };
        startup.Click += (_, _) =>
        {
            _settings.StartWithWindows = !_settings.StartWithWindows;
            ApplyStartWithWindows(_settings.StartWithWindows);
            _settings.Save();
        };
        var updates = new ToolStripMenuItem("Check for updates") { Checked = _settings.CheckForUpdates };
        updates.Click += (_, _) =>
        {
            _settings.CheckForUpdates = !_settings.CheckForUpdates;
            _settings.Save();
        };
        root.DropDownItems.Add(startup);
        root.DropDownItems.Add(updates);
        root.DropDownItems.Add(new ToolStripSeparator());
        root.DropDownItems.Add("Open log folder", null, (_, _) => OpenUrl(Log.Folder));
        return root;
    }

    void SaveAndApply()
    {
        _settings.Save();
        Reconcile();
    }

    static string NoiseName(NoiseMode mode, bool detailed = false) => mode switch
    {
        NoiseMode.DeepFilter => detailed ? "DeepFilterNet (strongest, for keyboards and voices)" : "DeepFilterNet",
        NoiseMode.RNNoise => detailed ? "RNNoise (recommended)" : "RNNoise",
        NoiseMode.WebRtc => detailed ? "WebRTC (basic)" : "WebRTC noise suppression",
        _ => detailed ? "Off" : "no noise removal",
    };

    static void ShowSetupHelp() => MessageBox.Show(
        "Better Mic sends your cleaned-up microphone to the virtual device \"CABLE Output\".\n\n" +
        "Discord (User Settings > Voice & Video):\n" +
        "  • Input Device: CABLE Output (VB-Audio Virtual Cable)\n" +
        "  • Turn OFF Noise Suppression, Echo Cancellation and Automatic Gain Control.\n" +
        "    Better Mic already does these; processing twice sounds worse.\n\n" +
        "Games with voice chat: pick \"CABLE Output\" as the microphone, or make it the Windows " +
        "default recording device (Better Mic keeps using your real mic).\n\n" +
        "To hear yourself: Sound settings > More sound settings > Recording > CABLE Output > " +
        "Properties > Listen > \"Listen to this device\".",
        $"{AppName} setup", MessageBoxButtons.OK, MessageBoxIcon.Information);

    void ShowBalloon(string title, string text, ToolTipIcon icon, Action? onClick = null)
    {
        _balloonClick = onClick;
        _tray.ShowBalloonTip(5000, title, text, icon);
    }

    async Task CheckForUpdate()
    {
        var tag = await UpdateChecker.CheckAsync();
        if (tag == null || tag == _availableUpdate) return;
        _availableUpdate = tag;
        ShowBalloon("Update available", $"{AppName} {tag} is available. Click to open the download page.",
            ToolTipIcon.Info, () => OpenUrl(UpdateChecker.ReleasesPage));
    }

    static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    // Classic Sound control panel, Playback tab (lists disabled devices via right-click)
    static void OpenSoundSettings() => Process.Start(new ProcessStartInfo("control.exe", "mmsys.cpl,,0") { UseShellExecute = true });

    // Recording tab: mic Properties > Levels
    static void OpenRecordingSettings() => Process.Start(new ProcessStartInfo("control.exe", "mmsys.cpl,,1") { UseShellExecute = true });

    static void ApplyStartWithWindows(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key == null) return;
        if (enable)
            key.SetValue("better-mic", $"\"{Application.ExecutablePath}\"");
        else
            key.DeleteValue("better-mic", throwOnMissingValue: false);
    }

    protected override void ExitThreadCore()
    {
        _reconcileTimer.Stop();
        _updateTimer.Stop();
        _clipTimer.Stop();
        _testForm?.Close();
        _engine.Dispose();
        _devices.Dispose();
        _tray.Visible = false;
        _tray.Icon?.Dispose();
        _tray.Dispose();
        base.ExitThreadCore();
    }
}
