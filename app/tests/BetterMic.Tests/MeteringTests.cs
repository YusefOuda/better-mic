using BetterMic.Audio;

namespace BetterMic.Tests;

public class ClipWarnerTests
{
    static readonly DateTime T0 = new(2026, 10, 9, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WarnsWhenClippingPilesUpWithinTheWindow()
    {
        var w = new ClipWarner();
        Assert.False(w.Update(0, T0));
        Assert.False(w.Update(3, T0.AddSeconds(1)));
        Assert.True(w.Update(6, T0.AddSeconds(2)));
    }

    [Fact]
    public void IgnoresOccasionalClipsSpreadOut()
    {
        var w = new ClipWarner();
        long count = 0;
        for (int s = 0; s < 120; s++)
        {
            if (s % 15 == 0) count++; // one clipped frame every 15 s
            Assert.False(w.Update(count, T0.AddSeconds(s)));
        }
    }

    [Fact]
    public void StaysQuietDuringCooldown()
    {
        var w = new ClipWarner();
        w.Update(0, T0);
        Assert.True(w.Update(10, T0.AddSeconds(1)));
        Assert.False(w.Update(20, T0.AddSeconds(2)));
        Assert.False(w.Update(30, T0.AddMinutes(10)));
        w.Update(30, T0.AddMinutes(31));
        Assert.True(w.Update(40, T0.AddMinutes(31).AddSeconds(1)));
    }

    [Fact]
    public void HandlesCounterResetAfterPipelineRestart()
    {
        var w = new ClipWarner();
        w.Update(100, T0);
        Assert.False(w.Update(0, T0.AddSeconds(1)));
        Assert.False(w.Update(2, T0.AddSeconds(2)));
        Assert.True(w.Update(5, T0.AddSeconds(3)));
    }
}

public class AudioLevelsTests
{
    [Fact]
    public void PeakIsLargestMagnitude() => Assert.Equal(0.8f, AudioLevels.Peak([0.1f, -0.8f, 0.5f]));

    [Fact]
    public void ClippingNeedsSeveralFullScaleSamples()
    {
        Assert.False(AudioLevels.IsClipped([1f, 0.2f, -1f, 0.1f]));
        Assert.True(AudioLevels.IsClipped([1f, 1f, -1f, 0.1f]));
    }

    [Fact]
    public void DbConversion()
    {
        Assert.Equal(0, AudioLevels.ToDb(1f), 3);
        Assert.Equal(-6.02, AudioLevels.ToDb(0.5f), 2);
        Assert.Equal(AudioLevels.SilenceDb, AudioLevels.ToDb(0f));
    }
}

public class PipelineCountersTests
{
    [Fact]
    public void TakePeaksReturnsMaxSinceLastCallThenResets()
    {
        var c = new PipelineCounters();
        c.RecordFrame(0, 0, 0, 0.2f, 0.1f, false);
        c.RecordFrame(0, 0, 0, 0.6f, 0.05f, true);
        Assert.Equal((0.6f, 0.1f), c.TakePeaks());
        Assert.Equal((0f, 0f), c.TakePeaks());
        Assert.Equal(1, c.Snapshot().ClippedFrames);
    }

    [Fact]
    public void SnapshotDerivesCpuAndDropouts()
    {
        var s = new PipelineSnapshot(100, 0.5, 2, Underruns: 2, Drops: 3);
        Assert.Equal(5.0, s.CpuPercent, 6);
        Assert.Equal(5, s.Dropouts);
    }
}

public class MicRecorderTests
{
    [Fact]
    public void FillsBothTracksThenStops()
    {
        var r = new MicRecorder(5);
        r.Write([1, 2, 3], [4, 5, 6]);
        Assert.False(r.IsComplete);
        r.Write([7, 8, 9], [10, 11, 12]);
        Assert.True(r.IsComplete);
        Assert.Equal([1f, 2, 3, 7, 8], r.Raw);
        Assert.Equal([4f, 5, 6, 10, 11], r.Processed);
        r.Write([0], [0]); // ignored once full
        Assert.Equal(1.0, r.Progress);
    }
}
