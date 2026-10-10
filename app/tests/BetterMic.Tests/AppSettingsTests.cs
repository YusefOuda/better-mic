using BetterMic.Native;

namespace BetterMic.Tests;

public class AppSettingsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "better-mic-tests-" + Guid.NewGuid().ToString("N"));
    string SettingsFile => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void DefaultsToRNNoise()
    {
        var s = new AppSettings();
        Assert.Equal(NoiseMode.RNNoise, s.NoiseMode);
        Assert.Equal(2, s.NoiseLevel);
        Assert.Equal(AecMode.Auto, s.EchoCancellation);
        Assert.True(s.ProcessingEnabled);
    }

    [Fact]
    public void RoundTripsWithEnumNames()
    {
        var s = new AppSettings { NoiseMode = NoiseMode.RNNoise, EchoCancellation = AecMode.Off, MicDeviceId = "id" };
        s.SaveTo(SettingsFile);
        Assert.Contains("\"RNNoise\"", File.ReadAllText(SettingsFile));

        var loaded = AppSettings.LoadFrom(SettingsFile);
        Assert.Equal(NoiseMode.RNNoise, loaded.NoiseMode);
        Assert.Equal(AecMode.Off, loaded.EchoCancellation);
        Assert.Equal("id", loaded.MicDeviceId);
    }

    [Fact]
    public void ClampsOutOfRangeValues()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsFile, """{ "NoiseLevel": 9, "NoiseMode": 42 }""");
        var loaded = AppSettings.LoadFrom(SettingsFile);
        Assert.Equal(3, loaded.NoiseLevel);
        Assert.Equal(NoiseMode.RNNoise, loaded.NoiseMode);
    }

    [Fact]
    public void CorruptFileGivesDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsFile, "{ not json");
        Assert.Equal(NoiseMode.RNNoise, AppSettings.LoadFrom(SettingsFile).NoiseMode);
    }

    [Fact]
    public void ConfigMapsToNative()
    {
        var c = new AppSettings { NoiseMode = NoiseMode.WebRtc, NoiseLevel = 1, AutoGain = false }.ToConfig(aecEnabled: true);
        Assert.Equal(1, c.AecEnabled);
        Assert.Equal((int)NoiseMode.WebRtc, c.NsMode);
        Assert.Equal(1, c.NsLevel);
        Assert.Equal(0, c.AgcEnabled);
        Assert.Equal(1, c.HpfEnabled);
    }
}
