using NAudio.CoreAudioApi;

namespace BetterMic.Audio;

// Plays processed mono frames into VB-Cable's "CABLE Input". Called from the mic thread only.
// Keeps ~30 ms buffered: enough to ride out scheduling jitter, small enough for voice chat.
sealed class CableSink : IDisposable
{
    const int Frame = Native.MicProcNative.FrameSize;
    const int TargetFrames = 3 * Frame;     // 30 ms
    const int ToleranceFrames = Frame;      // +-10 ms before drift correction kicks in
    const int BufferMs = 100;

    readonly AudioClient _client;
    readonly AudioRenderClient _render;
    readonly int _channels;
    readonly int _bufferFrames;
    readonly float[] _stretched = new float[Frame + 1];
    bool _started;

    public long Underruns { get; private set; }
    public long Drops { get; private set; }

    public CableSink(MMDevice device)
    {
        _client = Wasapi.Open(device, AudioClientStreamFlags.None, BufferMs, out _channels);
        _render = _client.AudioRenderClient;
        _bufferFrames = _client.BufferSize;
    }

    public unsafe void Write(ReadOnlySpan<float> frame)
    {
        int padding = _client.CurrentPadding;
        if (padding == 0)
        {
            // Start, or the mic stalled long enough to drain the buffer: refill to the target
            // with silence so playback doesn't immediately starve again.
            if (_started) Underruns++;
            WriteSilence(TargetFrames);
            padding = TargetFrames;
        }

        int length = DriftCorrector.OutputLength(padding, TargetFrames, ToleranceFrames, frame.Length);
        if (_bufferFrames - padding < length)
        {
            Drops++;
            return;
        }
        var mono = _stretched.AsSpan(0, length);
        DriftCorrector.Stretch(frame, mono);

        IntPtr buffer = _render.GetBuffer(length);
        ChannelMixer.Upmix(mono, _channels, new Span<float>((void*)buffer, length * _channels));
        _render.ReleaseBuffer(length, AudioClientBufferFlags.None);

        if (!_started)
        {
            _client.Start();
            _started = true;
        }
    }

    void WriteSilence(int frames)
    {
        frames = Math.Min(frames, _bufferFrames - _client.CurrentPadding);
        if (frames <= 0) return;
        _render.GetBuffer(frames);
        _render.ReleaseBuffer(frames, AudioClientBufferFlags.Silent);
    }

    public void Dispose()
    {
        try { if (_started) _client.Stop(); } catch { }
        _render.Dispose();
        _client.Dispose();
    }
}
