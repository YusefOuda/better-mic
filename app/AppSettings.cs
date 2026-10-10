using System.Text.Json;
using System.Text.Json.Serialization;
using BetterMic.Native;

namespace BetterMic;

class AppSettings
{
    // Off = mic passes through to VB-Cable unprocessed (Discord keeps working)
    public bool ProcessingEnabled { get; set; } = true;
    public NoiseMode NoiseMode { get; set; } = NoiseMode.RNNoise;
    // 0 low .. 3 max (all noise modes). Default High: Maximum gates word gaps to silence, which sounds choppy with AGC
    public int NoiseLevel { get; set; } = 2;
    public AecMode EchoCancellation { get; set; } = AecMode.Auto;
    public bool AutoGain { get; set; } = true;
    // Null = automatic (Windows default mic, skipping VB-Cable)
    public string? MicDeviceId { get; set; }
    public bool StartWithWindows { get; set; } = false;
    public bool CheckForUpdates { get; set; } = true;
    // Shown once on first run: how to point Discord at the virtual mic
    public bool SetupShown { get; set; } = false;

    static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "better-mic",
        "settings.json");

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load() => LoadFrom(SettingsPath);

    internal static AppSettings LoadFrom(string path)
    {
        try
        {
            if (File.Exists(path))
                return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new()).Sanitized();
        }
        catch { }
        return new();
    }

    public void Save() => SaveTo(SettingsPath);

    internal void SaveTo(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
        }
        catch { }
    }

    // Hand-edited or older files may hold out-of-range values; micproc rejects those.
    AppSettings Sanitized()
    {
        NoiseLevel = Math.Clamp(NoiseLevel, 0, 3);
        if (!Enum.IsDefined(NoiseMode)) NoiseMode = NoiseMode.RNNoise;
        if (!Enum.IsDefined(EchoCancellation)) EchoCancellation = AecMode.Auto;
        return this;
    }

    public MicProcConfig ToConfig(bool aecEnabled) => new()
    {
        AecEnabled = aecEnabled ? 1 : 0,
        NsMode = (int)NoiseMode,
        NsLevel = NoiseLevel,
        AgcEnabled = AutoGain ? 1 : 0,
        HpfEnabled = 1,
    };
}
