using NAudio.CoreAudioApi;

namespace BetterMic;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Used by the installer: exit code 0 if VB-Cable's "CABLE Input" is present, 1 if not
        if (args.Contains("--check-cable")) return HasVirtualCable() ? 0 : 1;
        // Diagnostics: runs the real pipeline headless and prints health once a second.
        // WinExe has no console, so redirect stdout: BetterMic.exe --selftest 10 > out.txt
        int selfTest = Array.IndexOf(args, "--selftest");
        if (selfTest >= 0)
            return SelfTest.Run(selfTest + 1 < args.Length && int.TryParse(args[selfTest + 1], out var s) ? s : 5);

        using var mutex = new Mutex(true, "better-mic-{6F1D2A9C-3B7E-4C51-9A80-D2E4F5A6B7C8}", out var firstInstance);
        if (!firstInstance) return 0;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        // Device notifications are posted through this; no Control exists yet to install it
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Application.Run(new TrayApp());
        return 0;
    }

    static bool HasVirtualCable()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                    if (DevicePolicy.IsVirtualCable(device.DeviceFriendlyName)) return true;
            }
        }
        catch { }
        return false;
    }
}
