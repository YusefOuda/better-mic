namespace BetterMic;

public enum AecMode { Auto, On, Off }

// Values of PKEY_AudioEndpoint_FormFactor (EndpointFormFactor in mmdeviceapi.h)
public enum FormFactor
{
    RemoteNetworkDevice = 0,
    Speakers = 1,
    LineLevel = 2,
    Headphones = 3,
    Microphone = 4,
    Headset = 5,
    Handset = 6,
    UnknownDigitalPassthrough = 7,
    Spdif = 8,
    DigitalAudioDisplayDevice = 9,
    Unknown = 10,
}

// IsOtherVirtual: another app's virtual device (e.g. NVIDIA Broadcast's mic), never picked automatically
record AudioDevice(string Id, string Name, bool IsVirtualCable, bool IsOtherVirtual = false);

// Pure device rules, kept out of the COM code so they can be unit tested.
static class DevicePolicy
{
    // VB-Cable endpoints keep this interface name even when the user renames "CABLE Output"/"CABLE Input".
    public static bool IsVirtualCable(string interfaceName) =>
        interfaceName.Contains("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase);

    // Virtual devices from other audio apps. Their "mic" is already processed (or not a mic at all),
    // so automatic selection skips them; the user can still pick one explicitly.
    static readonly string[] OtherVirtualDevices =
        ["NVIDIA Broadcast", "Steam Streaming", "VoiceMeeter", "VB-Audio", "Virtual Audio"];

    public static bool IsOtherVirtual(string interfaceName) =>
        !IsVirtualCable(interfaceName) &&
        OtherVirtualDevices.Any(n => interfaceName.Contains(n, StringComparison.OrdinalIgnoreCase));

    // Echo needs a speaker: with headphones/headset on, nothing from the output reaches the mic.
    public static bool IsAecEnabled(AecMode mode, FormFactor? outputFormFactor) => mode switch
    {
        AecMode.On => true,
        AecMode.Off => false,
        _ => outputFormFactor is not (FormFactor.Headphones or FormFactor.Headset or FormFactor.Handset),
    };

    // Chooses the physical mic to capture. Never returns VB-Cable: capturing our own output would
    // loop it back. Setting "CABLE Output" as the Windows default mic is the recommended setup,
    // so "automatic" has to look past it, and past other apps' virtual mics.
    public static string? PickMic(string? preferredId, IReadOnlyList<AudioDevice> activeCaptureDevices,
        string? defaultId, string? defaultCommunicationsId)
    {
        AudioDevice? Find(string? id) => id == null ? null : activeCaptureDevices.FirstOrDefault(d => d.Id == id);
        bool Physical(AudioDevice? d) => d is { IsVirtualCable: false, IsOtherVirtual: false };

        if (Find(preferredId) is { IsVirtualCable: false } chosen) return chosen.Id;
        if (Physical(Find(defaultId))) return defaultId;
        if (Physical(Find(defaultCommunicationsId))) return defaultCommunicationsId;
        return activeCaptureDevices.FirstOrDefault(Physical)?.Id;
    }

    // "CABLE Input" is the render side of VB-Cable; what we play there comes out of "CABLE Output".
    // VB-Cable 4.5 adds a 16-channel input too; prefer the regular one.
    public static string? PickCableInput(IReadOnlyList<AudioDevice> activeRenderDevices) =>
        activeRenderDevices.Where(d => d.IsVirtualCable)
            .OrderBy(d => d.Name.Contains("16 Ch", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault()?.Id;
}
