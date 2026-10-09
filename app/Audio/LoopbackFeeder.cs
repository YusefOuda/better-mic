using NAudio.CoreAudioApi;
using BetterMic.Native;

namespace BetterMic.Audio;

// Captures what the speakers play (WASAPI loopback) and feeds it to micproc as the echo
// reference. Polls instead of using events: loopback event delivery is unreliable while
// nothing is playing.
sealed class LoopbackFeeder : AudioThread
{
    const int BufferMs = 50;
    const int PollMs = 5;

    readonly string _renderId;
    readonly MicProcessor _processor;

    public LoopbackFeeder(string renderId, MicProcessor processor, Action<Exception> onError)
        : base("Loopback feeder", onError)
    {
        _renderId = renderId;
        _processor = processor;
        StartThread();
    }

    protected override unsafe void Run()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(_renderId);
        using var client = Wasapi.Open(device, AudioClientStreamFlags.Loopback, BufferMs, out int channels);
        using var capture = client.AudioCaptureClient;

        var accumulator = new FrameAccumulator(MicProcNative.FrameSize * channels);
        FrameAccumulator.FrameHandler onFrame = frame => _processor.ProcessRender(frame, channels);

        client.Start();
        try
        {
            while (!Stop.WaitOne(PollMs))
            {
                while (capture.GetNextPacketSize() > 0)
                {
                    IntPtr data = capture.GetBuffer(out int frames, out var flags);
                    if (flags.HasFlag(AudioClientBufferFlags.Silent))
                        accumulator.AppendSilence(frames * channels, onFrame);
                    else
                        accumulator.Append(new ReadOnlySpan<float>((void*)data, frames * channels), onFrame);
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
