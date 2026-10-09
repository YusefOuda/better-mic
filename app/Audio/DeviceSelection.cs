using NAudio.CoreAudioApi;

namespace BetterMic.Audio;

// Which devices the engine should use right now, resolved from the live device list and settings.
record DeviceSelection(
    string? MicId,
    string? MicName,
    string? CableId,
    string? OutputId,
    string? OutputName,
    FormFactor? OutputFormFactor,
    bool AecEnabled)
{
    public static DeviceSelection Resolve(Devices devices, AppSettings settings)
    {
        var captures = devices.Active(DataFlow.Capture);
        var renders = devices.Active(DataFlow.Render);
        string? cableId = DevicePolicy.PickCableInput(renders);
        string? micId = DevicePolicy.PickMic(settings.MicDeviceId, captures,
            devices.DefaultId(DataFlow.Capture, Role.Console),
            devices.DefaultId(DataFlow.Capture, Role.Communications));

        string? outputId = devices.DefaultId(DataFlow.Render, Role.Console);
        if (outputId == cableId) outputId = null; // nothing audible to cancel
        var formFactor = outputId != null ? devices.GetFormFactor(outputId) : null;
        bool aec = outputId != null && DevicePolicy.IsAecEnabled(settings.EchoCancellation, formFactor);

        return new DeviceSelection(micId, captures.FirstOrDefault(d => d.Id == micId)?.Name, cableId,
            outputId, renders.FirstOrDefault(d => d.Id == outputId)?.Name, formFactor, aec);
    }
}
