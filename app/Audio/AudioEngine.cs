using BetterMic.Native;

namespace BetterMic.Audio;

// Owns the native processor and the audio threads. Call from the UI thread only; audio thread
// failures are reported through Faulted (raised on the audio thread).
sealed class AudioEngine : IDisposable
{
    // Rough mic + loopback buffering; AEC3 finds the real delay itself.
    const int StreamDelayHintMs = 40;

    MicProcessor? _processor;
    MicPipeline? _mic;
    LoopbackFeeder? _loopback;
    volatile bool _processingEnabled = true;

    public event Action<string>? Faulted;

    public bool IsRunning => _mic != null;
    public bool IsLoopbackRunning => _loopback != null;

    public bool ProcessingEnabled
    {
        get => _processingEnabled;
        set => _processingEnabled = value;
    }

    // Starts (or restarts) mic -> VB-Cable. Throws if the native library can't be loaded.
    public void Start(string micId, string cableId, MicProcConfig config)
    {
        StopMic();
        if (_processor == null)
        {
            _processor = new MicProcessor(config);
            _processor.SetStreamDelayMs(StreamDelayHintMs);
        }
        else
        {
            _processor.SetConfig(config);
        }
        _mic = new MicPipeline(micId, cableId, _processor, () => _processingEnabled,
            e => Faulted?.Invoke($"Microphone stream stopped: {e.Message}"));
    }

    // Starts loopback capture of renderId for echo cancellation, or stops it when null.
    public void SetLoopback(string? renderId)
    {
        _loopback?.Dispose();
        _loopback = null;
        if (renderId != null && _processor != null)
            _loopback = new LoopbackFeeder(renderId, _processor,
                e => Faulted?.Invoke($"Speaker capture stopped: {e.Message}"));
    }

    public void Configure(MicProcConfig config) => _processor?.SetConfig(config);

    public MicProcStats? Stats => _processor?.GetStats();

    public PipelineSnapshot? Counters => _mic?.Counters.Snapshot();

    public void Stop()
    {
        SetLoopback(null);
        StopMic();
    }

    void StopMic()
    {
        _mic?.Dispose();
        _mic = null;
    }

    public void Dispose()
    {
        // Threads must be joined before the native processor they use is freed
        Stop();
        _processor?.Dispose();
        _processor = null;
    }
}
