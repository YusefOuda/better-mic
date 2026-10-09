using BetterMic.Audio;
using NAudio.Wave;

namespace BetterMic.Tests;

public class FrameAccumulatorTests
{
    [Fact]
    public void EmitsFixedFramesAcrossUnevenPackets()
    {
        var acc = new FrameAccumulator(4);
        var frames = new List<float[]>();
        FrameAccumulator.FrameHandler collect = f => frames.Add((float[])f.Clone());

        acc.Append([1, 2, 3], collect);
        Assert.Empty(frames);
        acc.Append([4, 5, 6, 7, 8, 9], collect);
        acc.AppendSilence(3, collect);

        Assert.Equal(3, frames.Count);
        Assert.Equal([1, 2, 3, 4], frames[0]);
        Assert.Equal([5, 6, 7, 8], frames[1]);
        Assert.Equal([9, 0, 0, 0], frames[2]);
    }

    [Fact]
    public void ResetDropsPartialFrame()
    {
        var acc = new FrameAccumulator(2);
        var count = 0;
        acc.Append([1], _ => count++);
        acc.Reset();
        acc.Append([2], _ => count++);
        Assert.Equal(0, count);
    }
}

public class DriftCorrectorTests
{
    [Theory]
    [InlineData(1440, 480)] // on target
    [InlineData(1920, 480)] // at the upper edge of the band
    [InlineData(960, 480)]  // at the lower edge
    [InlineData(1921, 479)] // too full: shorten
    [InlineData(959, 481)]  // too empty: lengthen
    public void AdjustsByOneSampleOutsideTolerance(int buffered, int expected) =>
        Assert.Equal(expected, DriftCorrector.OutputLength(buffered, 1440, 480, 480));

    [Fact]
    public void StretchKeepsEndpointsAndInterpolates()
    {
        float[] src = [0, 1, 2, 3, 4];
        var dst = new float[9];
        DriftCorrector.Stretch(src, dst);
        Assert.Equal(0f, dst[0]);
        Assert.Equal(4f, dst[^1]);
        Assert.Equal(0.5f, dst[1], 5);
        Assert.Equal(2f, dst[4], 5);
    }

    [Fact]
    public void StretchSameLengthCopies()
    {
        float[] src = [3, 1, 4];
        var dst = new float[3];
        DriftCorrector.Stretch(src, dst);
        Assert.Equal(src, dst);
    }

    [Fact]
    public void StretchShortensSmoothly()
    {
        var src = Enumerable.Range(0, 480).Select(i => (float)i).ToArray();
        var dst = new float[479];
        DriftCorrector.Stretch(src, dst);
        Assert.Equal(0f, dst[0]);
        Assert.Equal(479f, dst[^1], 3);
        for (int i = 1; i < dst.Length; i++) Assert.True(dst[i] > dst[i - 1]);
    }
}

public class ChannelMixerTests
{
    [Fact]
    public void DownmixAveragesChannels()
    {
        var mono = new float[2];
        ChannelMixer.Downmix([1, 3, -2, 2], 2, mono);
        Assert.Equal([2f, 0f], mono);
    }

    [Fact]
    public void UpmixDuplicates()
    {
        var stereo = new float[4];
        ChannelMixer.Upmix([0.5f, -1f], 2, stereo);
        Assert.Equal([0.5f, 0.5f, -1f, -1f], stereo);
    }
}

public class WasapiFormatTests
{
    // micproc and our loops read raw float32 from WASAPI buffers, so the requested format must be IEEE float
    [Fact]
    public void Float48kIsIeeeFloat()
    {
        var format = (WaveFormatExtensible)Wasapi.Float48k(2);
        Assert.Equal(48000, format.SampleRate);
        Assert.Equal(32, format.BitsPerSample);
        Assert.Equal(2, format.Channels);
        Assert.Equal(new Guid("00000003-0000-0010-8000-00aa00389b71"), format.SubFormat); // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT
    }
}
