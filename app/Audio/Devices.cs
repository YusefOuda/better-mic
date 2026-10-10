using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace BetterMic.Audio;

// Ready: VB-Cable's playback side ("CABLE Input") is active. Disabled: VB-Cable is installed but
// that endpoint is disabled in Sound settings. Missing: not installed.
enum CableState { Ready, Disabled, Missing }

// Device enumeration and change notifications. Use from the UI thread.
sealed class Devices : IDisposable
{
    readonly MMDeviceEnumerator _enumerator = new();
    readonly Watcher _watcher;

    // Raised on the UI thread after any endpoint change (added/removed/state/default), with a description.
    public event Action<string>? Changed;

    public Devices()
    {
        var ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Create on the UI thread");
        _watcher = new Watcher(change => ui.Post(_ => Changed?.Invoke(change), null));
        _enumerator.RegisterEndpointNotificationCallback(_watcher);
    }

    public List<AudioDevice> Active(DataFlow flow)
    {
        var list = new List<AudioDevice>();
        foreach (var d in _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (d)
            {
                try
                {
                    string iface = d.DeviceFriendlyName;
                    list.Add(new AudioDevice(d.ID, d.FriendlyName, DevicePolicy.IsVirtualCable(iface),
                        DevicePolicy.IsOtherVirtual(iface)));
                }
                catch (Exception e)
                {
                    // Removed while we were enumerating; the removal notification triggers another pass
                    Log.Error($"Skipping {flow} device that couldn't be read: {e.Message}");
                }
            }
        }
        return list;
    }

    public CableState GetCableState() => GetCableState(_enumerator);

    public static CableState GetCableState(MMDeviceEnumerator enumerator)
    {
        bool Any(DataFlow flow, DeviceState states)
        {
            foreach (var d in enumerator.EnumerateAudioEndPoints(flow, states))
            {
                using (d)
                {
                    try
                    {
                        if (DevicePolicy.IsVirtualCable(d.DeviceFriendlyName)) return true;
                    }
                    catch { } // removed while enumerating
                }
            }
            return false;
        }

        if (Any(DataFlow.Render, DeviceState.Active)) return CableState.Ready;
        if (Any(DataFlow.All, DeviceState.Active | DeviceState.Disabled)) return CableState.Disabled;
        return CableState.Missing;
    }

    public string? DefaultId(DataFlow flow, Role role)
    {
        try
        {
            using var d = _enumerator.GetDefaultAudioEndpoint(flow, role);
            return d.ID;
        }
        catch
        {
            return null; // no device of that kind
        }
    }

    public FormFactor? GetFormFactor(string deviceId)
    {
        try
        {
            using var d = _enumerator.GetDevice(deviceId);
            var key = PropertyKeys.PKEY_AudioEndpoint_FormFactor;
            return d.Properties.Contains(key) ? (FormFactor)Convert.ToInt32(d.Properties[key].Value) : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        try { _enumerator.UnregisterEndpointNotificationCallback(_watcher); } catch { }
        _enumerator.Dispose();
    }

    // Called by the audio service on its own threads; must return quickly.
    sealed class Watcher(Action<string> notify) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => notify($"{deviceId} is now {newState}");
        public void OnDeviceAdded(string pwstrDeviceId) => notify($"{pwstrDeviceId} added");
        public void OnDeviceRemoved(string deviceId) => notify($"{deviceId} removed");
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) =>
            notify($"default {flow} {role} device is now {defaultDeviceId ?? "none"}");
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
