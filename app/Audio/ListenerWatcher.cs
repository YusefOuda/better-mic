using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace BetterMic.Audio;

// Reports whether any app is recording from VB-Cable's capture side ("CABLE Output"), so the
// pipeline only runs (and only holds Windows' "audio stream in use" wake lock) while someone listens.
// Event-driven: session-created and session state-change notifications trigger a recount; a slow
// periodic recount covers apps that crash without their session going inactive.
// All COM objects live on this thread: session notifications are only delivered to MTA clients.
sealed class ListenerWatcher : IDisposable
{
    static readonly TimeSpan SafetyRecount = TimeSpan.FromSeconds(30);

    readonly IReadOnlyList<string> _deviceIds;
    readonly Action<bool> _onChanged;
    readonly Thread _thread;
    readonly ManualResetEvent _stop = new(false);
    readonly AutoResetEvent _recount = new(false);
    bool? _listening;

    public IReadOnlyList<string> DeviceIds => _deviceIds;

    // onChanged(listening) is raised on the calling thread's SynchronizationContext: once with the
    // initial state, then on every change
    public ListenerWatcher(IReadOnlyList<string> captureDeviceIds, Action<bool> onChanged)
    {
        var ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Create on the UI thread");
        _deviceIds = captureDeviceIds;
        _onChanged = listening => ui.Post(_ => onChanged(listening), null);
        _thread = new Thread(Run) { Name = "Listener watcher", IsBackground = true };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    void Run()
    {
        var watched = new List<Watched>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var id in _deviceIds)
            {
                try
                {
                    var device = enumerator.GetDevice(id);
                    var manager = device.AudioSessionManager;
                    manager.OnSessionCreated += (_, _) => _recount.Set();
                    watched.Add(new Watched(device, manager));
                }
                catch (Exception e)
                {
                    Log.Error($"Can't watch listeners on {id}", e);
                }
            }

            var handles = new WaitHandle[] { _stop, _recount };
            do
            {
                bool listening = false;
                foreach (var w in watched) listening |= w.Recount(() => _recount.Set());
                if (listening != _listening)
                {
                    _listening = listening;
                    _onChanged(listening);
                }
            }
            while (WaitHandle.WaitAny(handles, SafetyRecount) != 0);
        }
        catch (Exception e)
        {
            // Without a watcher we can't tell; run the pipeline rather than leave apps with silence
            Log.Error("Listener watcher failed; staying on", e);
            if (_listening != true) _onChanged(true);
        }
        finally
        {
            foreach (var w in watched) w.Dispose();
        }
    }

    public void Dispose()
    {
        _stop.Set();
        if (_thread.Join(TimeSpan.FromSeconds(2)))
        {
            _stop.Dispose();
            _recount.Dispose();
        }
    }

    // One capture endpoint and the sessions on it we've subscribed to, by session instance id
    sealed class Watched(MMDevice device, AudioSessionManager manager) : IDisposable
    {
        static readonly uint OwnPid = (uint)Environment.ProcessId;
        readonly Dictionary<string, (AudioSessionControl Session, StateHandler Handler)> _sessions = new();

        // True if another process is actively recording from this device. Subscribes to new sessions.
        public bool Recount(Action changed)
        {
            manager.RefreshSessions();
            var sessions = manager.Sessions;
            var seen = new HashSet<string>();
            bool active = false;
            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                string id = session.GetSessionInstanceIdentifier;
                seen.Add(id);
                if (!_sessions.ContainsKey(id))
                {
                    var handler = new StateHandler(changed);
                    session.RegisterEventClient(handler);
                    _sessions[id] = (session, handler);
                }
                active |= session.State == AudioSessionState.AudioSessionStateActive && session.GetProcessID != OwnPid;
            }
            foreach (var gone in _sessions.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                Unsubscribe(_sessions[gone]);
                _sessions.Remove(gone);
            }
            return active;
        }

        static void Unsubscribe((AudioSessionControl Session, StateHandler Handler) s)
        {
            try { s.Session.UnRegisterEventClient(s.Handler); } catch { }
        }

        public void Dispose()
        {
            foreach (var s in _sessions.Values) Unsubscribe(s);
            _sessions.Clear();
            manager.Dispose();
            device.Dispose();
        }
    }

    sealed class StateHandler(Action changed) : IAudioSessionEventsHandler
    {
        public void OnStateChanged(AudioSessionState state) => changed();
        public void OnSessionDisconnected(AudioSessionDisconnectReason reason) => changed();
        public void OnVolumeChanged(float volume, bool isMuted) { }
        public void OnDisplayNameChanged(string displayName) { }
        public void OnIconPathChanged(string iconPath) { }
        public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
        public void OnGroupingParamChanged(ref Guid groupingId) { }
    }
}
