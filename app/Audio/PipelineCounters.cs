using System.Diagnostics;

namespace BetterMic.Audio;

// Health and level counters written by the mic thread (single writer) and read from the UI thread.
sealed class PipelineCounters
{
    long _frames;
    long _processTicks;
    long _maxProcessTicks;
    long _underruns;
    long _drops;
    long _clippedFrames;
    // Peak amplitudes since the last TakePeaks, stored as float bits for Interlocked
    int _rawPeakBits;
    int _outputPeakBits;

    public void RecordFrame(long processTicks, long underruns, long drops, float rawPeak, float outputPeak, bool clipped)
    {
        Volatile.Write(ref _frames, _frames + 1);
        Volatile.Write(ref _processTicks, _processTicks + processTicks);
        if (processTicks > _maxProcessTicks) Volatile.Write(ref _maxProcessTicks, processTicks);
        Volatile.Write(ref _underruns, underruns);
        Volatile.Write(ref _drops, drops);
        if (clipped) Volatile.Write(ref _clippedFrames, _clippedFrames + 1);
        RaisePeak(ref _rawPeakBits, rawPeak);
        RaisePeak(ref _outputPeakBits, outputPeak);
    }

    static void RaisePeak(ref int bits, float value)
    {
        int current = Volatile.Read(ref bits);
        while (value > BitConverter.Int32BitsToSingle(current))
        {
            int seen = Interlocked.CompareExchange(ref bits, BitConverter.SingleToInt32Bits(value), current);
            if (seen == current) return;
            current = seen;
        }
    }

    // Peak levels (0..1) since the previous call, for meters
    public (float Raw, float Output) TakePeaks() => (
        BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _rawPeakBits, 0)),
        BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _outputPeakBits, 0)));

    public PipelineSnapshot Snapshot()
    {
        long frames = Volatile.Read(ref _frames);
        double ms = 1000.0 / Stopwatch.Frequency;
        return new PipelineSnapshot(
            frames,
            frames == 0 ? 0 : Volatile.Read(ref _processTicks) * ms / frames,
            Volatile.Read(ref _maxProcessTicks) * ms,
            Volatile.Read(ref _underruns),
            Volatile.Read(ref _drops),
            Volatile.Read(ref _clippedFrames));
    }
}

// MeanProcessMs/MaxProcessMs: micproc time per 10 ms frame. Underruns: VB-Cable buffer ran dry
// (mic stalled). Drops: buffer full, frame discarded. ClippedFrames: raw mic frames at full scale.
record PipelineSnapshot(long Frames, double MeanProcessMs, double MaxProcessMs, long Underruns, long Drops,
    long ClippedFrames = 0)
{
    // Share of one CPU core spent in micproc (each frame is 10 ms of audio)
    public double CpuPercent => MeanProcessMs / 10.0 * 100.0;
    public long Dropouts => Underruns + Drops;
}
