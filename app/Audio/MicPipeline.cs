using System.Diagnostics;
using NAudio.CoreAudioApi;
using BetterMic.Native;

namespace BetterMic.Audio;

// Mic capture -> micproc -> VB-Cable, all on one thread driven by the mic's WASAPI events.
sealed class MicPipeline : AudioThread
{
    const int BufferMs = 20;

    readonly string _micId;
    readonly string _cableId;
    readonly MicProcessor _processor;
    readonly Func<bool> _processingEnabled;
    readonly Func<bool> _muted;
    readonly float[] _rawCopy = new float[MicProcNative.FrameSize];

    public PipelineCounters Counters { get; } = new();

    // Set by the UI to capture the next few seconds; the mic thread fills it
    public volatile MicRecorder? Recorder;

    public MicPipeline(string micId, string cableId, MicProcessor processor, Func<bool> processingEnabled,
        Func<bool> muted, Action<Exception> onError) : base("Mic pipeline", onError)
    {
        _micId = micId;
        _cableId = cableId;
        _processor = processor;
        _processingEnabled = processingEnabled;
        _muted = muted;
        StartThread();
    }

    protected override unsafe void Run()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var micDevice = enumerator.GetDevice(_micId);
        using var cableDevice = enumerator.GetDevice(_cableId);
        using var sink = new CableSink(cableDevice);
        using var client = Wasapi.Open(micDevice, AudioClientStreamFlags.EventCallback, BufferMs, out int channels);
        using var capture = client.AudioCaptureClient;
        using var packetReady = new AutoResetEvent(false);
        client.SetEventHandle(packetReady.SafeWaitHandle.DangerousGetHandle());

        var accumulator = new FrameAccumulator(MicProcNative.FrameSize);
        var mono = new float[client.BufferSize];
        FrameAccumulator.FrameHandler onFrame = frame =>
        {
            float rawPeak = AudioLevels.Peak(frame);
            bool clipped = AudioLevels.IsClipped(frame);
            var recorder = Recorder;
            if (recorder != null) frame.CopyTo(_rawCopy, 0);

            long start = Stopwatch.GetTimestamp();
            if (_processingEnabled()) _processor.ProcessCapture(frame);
            long elapsed = Stopwatch.GetTimestamp() - start;

            float outputPeak = AudioLevels.Peak(frame);
            recorder?.Write(_rawCopy, frame);
            // Muting keeps processing running (AEC/NS/AGC state stays warm) and sends silence
            if (_muted()) Array.Clear(frame);
            sink.Write(frame);
            Counters.RecordFrame(elapsed, sink.Underruns, sink.Drops, rawPeak, outputPeak, clipped);
        };
        WaitHandle[] waits = [Stop, packetReady];

        client.Start();
        try
        {
            while (true)
            {
                int signaled = WaitHandle.WaitAny(waits, 200);
                if (signaled == 0) break;
                int packet;
                while ((packet = capture.GetNextPacketSize()) > 0)
                {
                    IntPtr data = capture.GetBuffer(out int frames, out var flags);
                    if (flags.HasFlag(AudioClientBufferFlags.Silent))
                    {
                        accumulator.AppendSilence(frames, onFrame);
                    }
                    else
                    {
                        var samples = new ReadOnlySpan<float>((void*)data, frames * channels);
                        ChannelMixer.Downmix(samples, channels, mono);
                        accumulator.Append(mono.AsSpan(0, frames), onFrame);
                    }
                    capture.ReleaseBuffer(frames);
                }
            }
        }
        finally
        {
            client.Stop();
        }
    }
}
