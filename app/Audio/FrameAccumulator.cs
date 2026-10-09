namespace BetterMic.Audio;

// Collects samples from WASAPI packets of any size into fixed-size frames (10 ms for micproc).
// The frame array passed to the callback is reused; consumers must not keep it.
sealed class FrameAccumulator
{
    readonly float[] _frame;
    int _fill;

    public FrameAccumulator(int frameSamples) => _frame = new float[frameSamples];

    public int FrameSamples => _frame.Length;

    public delegate void FrameHandler(float[] frame);

    public void Append(ReadOnlySpan<float> samples, FrameHandler onFrame)
    {
        while (!samples.IsEmpty)
        {
            int n = Math.Min(samples.Length, _frame.Length - _fill);
            samples[..n].CopyTo(_frame.AsSpan(_fill));
            _fill += n;
            samples = samples[n..];
            if (_fill == _frame.Length)
            {
                onFrame(_frame);
                _fill = 0;
            }
        }
    }

    // Appends `samples` zeros (used for packets flagged AUDCLNT_BUFFERFLAGS_SILENT)
    public void AppendSilence(int samples, FrameHandler onFrame)
    {
        while (samples > 0)
        {
            int n = Math.Min(samples, _frame.Length - _fill);
            _frame.AsSpan(_fill, n).Clear();
            _fill += n;
            samples -= n;
            if (_fill == _frame.Length)
            {
                onFrame(_frame);
                _fill = 0;
            }
        }
    }

    public void Reset() => _fill = 0;
}
