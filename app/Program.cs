using NAudio.CoreAudioApi;
using BetterMic.Audio;

namespace BetterMic;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Used by the installer: exit code 0 = VB-Cable ready, 1 = not installed, 2 = playback side disabled
        if (args.Contains("--check-cable")) return CheckCable();
        // Diagnostics: runs the real pipeline headless and prints health once a second.
        // WinExe has no console, so redirect stdout: BetterMic.exe --selftest 10 > out.txt
        int selfTest = Array.IndexOf(args, "--selftest");
        if (selfTest >= 0)
            return SelfTest.Run(selfTest + 1 < args.Length && int.TryParse(args[selfTest + 1], out var s) ? s : 5);

        using var mutex = new Mutex(true, "better-mic-{6F1D2A9C-3B7E-4C51-9A80-D2E4F5A6B7C8}", out var firstInstance);
        if (!firstInstance) return 0;

        Log.Info($"Better Mic {UpdateChecker.CurrentVersion} starting");
        // A modal Continue/Quit dialog in the middle of a game helps nobody: log UI-thread
        // exceptions and keep running instead
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("Unhandled exception on the UI thread", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error($"Unhandled exception (terminating: {e.IsTerminating})", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Error("Unobserved task exception", e.Exception);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        // Device notifications are posted through this; no Control exists yet to install it
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Application.Run(new TrayApp());
        Log.Info("Exiting");
        return 0;
    }

    static int CheckCable()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return Devices.GetCableState(enumerator) switch
            {
                CableState.Ready => 0,
                CableState.Disabled => 2,
                _ => 1,
            };
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"check-cable: {e.Message}");
            return 1;
        }
    }
}
