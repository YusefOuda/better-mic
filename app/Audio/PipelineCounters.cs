using System.Diagnostics;

namespace BetterMic.Audio;

// Health counters written by the mic thread (single writer) and read from any thread.
sealed class PipelineCounters
{
    long _frames;
    long _processTicks;
    long _maxProcessTicks;
    long _underruns;
    long _drops;

    public void RecordFrame(long processTicks, long underruns, long drops)
    {
        Volatile.Write(ref _frames, _frames + 1);
        Volatile.Write(ref _processTicks, _processTicks + processTicks);
        if (processTicks > _maxProcessTicks) Volatile.Write(ref _maxProcessTicks, processTicks);
        Volatile.Write(ref _underruns, underruns);
        Volatile.Write(ref _drops, drops);
    }

    public PipelineSnapshot Snapshot()
    {
        long frames = Volatile.Read(ref _frames);
        double ms = 1000.0 / Stopwatch.Frequency;
        return new PipelineSnapshot(
            frames,
            frames == 0 ? 0 : Volatile.Read(ref _processTicks) * ms / frames,
            Volatile.Read(ref _maxProcessTicks) * ms,
            Volatile.Read(ref _underruns),
            Volatile.Read(ref _drops));
    }
}

// MeanProcessMs/MaxProcessMs: micproc time per 10 ms frame. Underruns: VB-Cable buffer ran dry
// (mic stalled). Drops: buffer full, frame discarded.
record PipelineSnapshot(long Frames, double MeanProcessMs, double MaxProcessMs, long Underruns, long Drops);
