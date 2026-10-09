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

record AudioDevice(string Id, string Name, bool IsVirtualCable);

// Pure device rules, kept out of the COM code so they can be unit tested.
static class DevicePolicy
{
    // VB-Cable endpoints keep this interface name even when the user renames "CABLE Output"/"CABLE Input".
    public static bool IsVirtualCable(string interfaceName) =>
        interfaceName.Contains("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase);

    // Echo needs a speaker: with headphones/headset on, nothing from the output reaches the mic.
    public static bool IsAecEnabled(AecMode mode, FormFactor? outputFormFactor) => mode switch
    {
        AecMode.On => true,
        AecMode.Off => false,
        _ => outputFormFactor is not (FormFactor.Headphones or FormFactor.Headset or FormFactor.Handset),
    };

    // Chooses the physical mic to capture. Never returns VB-Cable: capturing our own output would
    // loop it back. Setting "CABLE Output" as the Windows default mic is the recommended setup,
    // so "automatic" has to look past it.
    public static string? PickMic(string? preferredId, IReadOnlyList<AudioDevice> activeCaptureDevices,
        string? defaultId, string? defaultCommunicationsId)
    {
        bool Usable(string? id) => id != null && activeCaptureDevices.Any(d => d.Id == id && !d.IsVirtualCable);

        if (Usable(preferredId)) return preferredId;
        if (Usable(defaultId)) return defaultId;
        if (Usable(defaultCommunicationsId)) return defaultCommunicationsId;
        return activeCaptureDevices.FirstOrDefault(d => !d.IsVirtualCable)?.Id;
    }

    // "CABLE Input" is the render side of VB-Cable; what we play there comes out of "CABLE Output".
    public static string? PickCableInput(IReadOnlyList<AudioDevice> activeRenderDevices) =>
        activeRenderDevices.FirstOrDefault(d => d.IsVirtualCable)?.Id;
}
