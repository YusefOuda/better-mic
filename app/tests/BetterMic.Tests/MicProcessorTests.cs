using BetterMic.Native;

namespace BetterMic.Tests;

// Exercises micproc.dll through the P/Invoke layer (needs build\micproc.dll from scripts\build.cmd).
public class MicProcessorTests
{
    static MicProcConfig Config(NoiseMode mode) =>
        new AppSettings { NoiseMode = mode, AutoGain = false }.ToConfig(aecEnabled: true);

    [Fact]
    public void ApiVersionMatches() => Assert.Equal(MicProcNative.ApiVersion, MicProcNative.ApiVersionOf());

    [Theory]
    [InlineData(NoiseMode.DeepFilter)]
    [InlineData(NoiseMode.RNNoise)]
    [InlineData(NoiseMode.WebRtc)]
    public void RemovesSteadyNoise(NoiseMode mode)
    {
        using var p = new MicProcessor(Config(mode));
        var rng = new Random(7);
        var frame = new float[MicProcNative.FrameSize];
        var render = new float[MicProcNative.FrameSize * 2];
        double inEnergy = 0, outEnergy = 0;
        for (int f = 0; f < 400; f++)
        {
            for (int i = 0; i < frame.Length; i++) frame[i] = (float)(rng.NextDouble() * 2 - 1) * 0.05f;
            bool measure = f >= 300;
            if (measure) inEnergy += frame.Sum(x => (double)x * x);
            Assert.Equal(0, p.ProcessRender(render, 2));
            Assert.Equal(0, p.ProcessCapture(frame));
            if (measure) outEnergy += frame.Sum(x => (double)x * x);
        }
        double reductionDb = 10 * Math.Log10(inEnergy / Math.Max(outEnergy, 1e-20));
        Assert.True(reductionDb > 6, $"{mode}: only {reductionDb:F1} dB reduction");
    }

    [Fact]
    public void RejectsInvalidConfig()
    {
        using var p = new MicProcessor(Config(NoiseMode.Off));
        var bad = Config(NoiseMode.Off);
        bad.NsLevel = 7;
        Assert.Throws<InvalidOperationException>(() => p.SetConfig(bad));
    }

    [Fact]
    public void UseAfterDisposeThrows()
    {
        var p = new MicProcessor(Config(NoiseMode.Off));
        p.Dispose();
        p.Dispose(); // idempotent
        Assert.Throws<ObjectDisposedException>(() => p.ProcessCapture(new float[MicProcNative.FrameSize]));
    }
}
