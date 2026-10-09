using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using BetterMic.Audio;
using BetterMic.Native;

namespace BetterMic;

// "Test microphone": live meters for the raw and processed mic, and a short recording that can be
// played back both ways to hear what processing does.
sealed class MicTestForm : Form
{
    const double RecordSeconds = 5;
    const double MeterFloorDb = -60;
    // Meter falls this many dB per tick (30 fps) so it's readable instead of flickering
    const double MeterFallDbPerTick = 1.5;

    readonly AudioEngine _engine;
    readonly Func<string?> _problem;
    readonly LevelMeter _rawMeter = new();
    readonly LevelMeter _outputMeter = new();
    readonly Label _rawText = new() { AutoSize = true };
    readonly Label _outputText = new() { AutoSize = true };
    readonly Label _clipText = new() { AutoSize = true, ForeColor = Color.Firebrick, Font = new Font(DefaultFont, FontStyle.Bold) };
    readonly Label _status = new() { AutoSize = false, Height = 36 };
    readonly Label _load = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    readonly Button _record = new() { Text = "Record 5 seconds", AutoSize = true };
    readonly Button _playRaw = new() { Text = "Play original", AutoSize = true, Enabled = false };
    readonly Button _playProcessed = new() { Text = "Play processed", AutoSize = true, Enabled = false };
    readonly Button _stop = new() { Text = "Stop", AutoSize = true, Enabled = false };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };

    double _rawDb = MeterFloorDb, _outputDb = MeterFloorDb;
    DateTime _clipUntil;
    MicRecorder? _recorder;
    float[]? _rawTake, _processedTake;
    WasapiOut? _player;

    public MicTestForm(AudioEngine engine, Func<string?> problem)
    {
        _engine = engine;
        _problem = problem;
        Text = "Better Mic: test microphone";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(460, 300);
        Padding = new Padding(16);
        ShowIcon = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true };
        layout.Controls.Add(new Label { Text = "Microphone (raw)", AutoSize = true });
        layout.Controls.Add(MeterRow(_rawMeter, _rawText, _clipText));
        layout.Controls.Add(new Label { Text = "After processing (what others hear)", AutoSize = true, Margin = new Padding(0, 10, 0, 0) });
        layout.Controls.Add(MeterRow(_outputMeter, _outputText, null));

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 14, 0, 0), WrapContents = false };
        buttons.Controls.AddRange([_record, _playRaw, _playProcessed, _stop]);
        layout.Controls.Add(buttons);
        _status.Width = 420;
        layout.Controls.Add(_status);
        layout.Controls.Add(_load);
        Controls.Add(layout);

        _record.Click += (_, _) => StartRecording();
        _playRaw.Click += (_, _) => Play(_rawTake);
        _playProcessed.Click += (_, _) => Play(_processedTake);
        _stop.Click += (_, _) => StopPlayback();
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        SetStatus("Speak normally and watch both meters. Record a few seconds to compare before and after.");
    }

    Control MeterRow(LevelMeter meter, Label value, Label? extra)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
        meter.Size = new Size(300, 18);
        meter.Margin = new Padding(0, 0, 8, 0);
        row.Controls.Add(meter);
        row.Controls.Add(value);
        if (extra != null) row.Controls.Add(extra);
        return row;
    }

    void Tick()
    {
        var peaks = _engine.TakePeaks();
        _rawDb = Follow(_rawDb, peaks?.Raw ?? 0);
        _outputDb = Follow(_outputDb, peaks?.Output ?? 0);
        if ((peaks?.Raw ?? 0) >= AudioLevels.ClipThreshold) _clipUntil = DateTime.UtcNow.AddSeconds(2);

        _rawMeter.Level = Fraction(_rawDb);
        _outputMeter.Level = Fraction(_outputDb);
        _rawText.Text = DbText(_rawDb);
        _outputText.Text = DbText(_outputDb);
        _clipText.Text = DateTime.UtcNow < _clipUntil ? "CLIPPING" : "";

        var counters = _engine.Counters;
        _load.Text = counters != null
            ? $"Processing load: {counters.CpuPercent:F1}% of one CPU core · dropouts: {counters.Dropouts}"
            : "";
        string? problem = _problem();
        _record.Enabled = problem == null && _recorder == null;
        if (problem != null && _recorder == null) SetStatus($"Not running: {problem}");

        if (_recorder is { IsComplete: true } done)
        {
            _rawTake = done.Raw;
            _processedTake = done.Processed;
            _recorder = null;
            _playRaw.Enabled = _playProcessed.Enabled = true;
            SetStatus("Recorded. Play both to compare. Playback uses your default output device.");
        }
        else if (_recorder != null)
        {
            SetStatus($"Recording… {_recorder.Progress * RecordSeconds:F1} s");
        }
    }

    // Instant attack, slow release
    static double Follow(double currentDb, float peak) =>
        Math.Max(AudioLevels.ToDb(peak), Math.Max(MeterFloorDb, currentDb - MeterFallDbPerTick));

    static float Fraction(double db) => (float)Math.Clamp((db - MeterFloorDb) / -MeterFloorDb, 0, 1);

    static string DbText(double db) => db <= MeterFloorDb ? "  -∞ dB" : $"{db,5:F0} dB";

    void SetStatus(string text) => _status.Text = text;

    void StartRecording()
    {
        StopPlayback();
        _recorder = _engine.StartRecording(RecordSeconds);
        if (_recorder == null) SetStatus("The microphone isn't running.");
    }

    void Play(float[]? samples)
    {
        if (samples == null) return;
        StopPlayback();
        try
        {
            _player = new WasapiOut(AudioClientShareMode.Shared, 100);
            _player.Init(new MonoToStereoSampleProvider(new ArraySampleProvider(samples)));
            _player.PlaybackStopped += (_, _) =>
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(() => _stop.Enabled = false);
            };
            _player.Play();
            _stop.Enabled = true;
        }
        catch (Exception e)
        {
            StopPlayback();
            SetStatus($"Couldn't play back: {e.Message}");
        }
    }

    void StopPlayback()
    {
        _player?.Stop();
        _player?.Dispose();
        _player = null;
        _stop.Enabled = false;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        StopPlayback();
        base.OnFormClosed(e);
    }

    sealed class ArraySampleProvider(float[] samples) : ISampleProvider
    {
        int _position;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(MicProcNative.SampleRate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, samples.Length - _position);
            Array.Copy(samples, _position, buffer, offset, n);
            _position += n;
            return n;
        }
    }

    // Horizontal bar: green, then yellow above -12 dB, red above -3 dB
    sealed class LevelMeter : Control
    {
        float _level;

        public LevelMeter()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public float Level
        {
            get => _level;
            set
            {
                if (Math.Abs(value - _level) < 0.002f) return;
                _level = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.FromArgb(40, 40, 40));
            int width = (int)(Width * _level);
            int yellowAt = (int)(Width * Fraction(-12));
            int redAt = (int)(Width * Fraction(-3));
            using var green = new SolidBrush(Color.FromArgb(80, 200, 120));
            using var yellow = new SolidBrush(Color.FromArgb(230, 200, 60));
            using var red = new SolidBrush(Color.FromArgb(235, 70, 60));
            g.FillRectangle(green, 0, 0, Math.Min(width, yellowAt), Height);
            if (width > yellowAt) g.FillRectangle(yellow, yellowAt, 0, Math.Min(width, redAt) - yellowAt, Height);
            if (width > redAt) g.FillRectangle(red, redAt, 0, width - redAt, Height);
        }
    }
}
