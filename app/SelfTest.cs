using BetterMic.Audio;

namespace BetterMic;

// `BetterMic.exe --selftest [seconds]`: mic -> micproc -> VB-Cable with the saved settings, no UI.
// Nothing is recorded. Exit code 0 = audio flowed without faults or dropped frames.
static class SelfTest
{
    public static int Run(int seconds)
    {
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var settings = AppSettings.Load();
        using var devices = new Devices();
        var sel = DeviceSelection.Resolve(devices, settings);

        Console.WriteLine($"mic:    {sel.MicName ?? "(none)"}");
        Console.WriteLine($"cable:  {(sel.CableId != null ? "found" : "(missing)")}");
        Console.WriteLine($"output: {sel.OutputName ?? "(none)"} [{sel.OutputFormFactor?.ToString() ?? "?"}]");
        Console.WriteLine($"noise:  {settings.NoiseMode} level {settings.NoiseLevel}, AEC {(sel.AecEnabled ? "on" : "off")} " +
                          $"({settings.EchoCancellation}), AGC {(settings.AutoGain ? "on" : "off")}");
        if (sel.MicId == null || sel.CableId == null) return 2;

        string? fault = null;
        using var engine = new AudioEngine();
        engine.Faulted += m => fault = m;
        engine.Start(sel.MicId, sel.CableId, settings.ToConfig(sel.AecEnabled));
        if (sel.AecEnabled) engine.SetLoopback(sel.OutputId);

        PipelineSnapshot? last = null;
        for (int i = 1; i <= seconds && fault == null; i++)
        {
            Thread.Sleep(1000);
            last = engine.Counters;
            var stats = engine.Stats;
            Console.WriteLine($"{i,3}s  frames {last?.Frames,5}  proc {last?.MeanProcessMs,5:F2} ms (max {last?.MaxProcessMs,5:F2})  " +
                              $"underruns {last?.Underruns}  drops {last?.Drops}  " +
                              $"ns_snr {stats?.NsSnrDb,6:F1} dB  aec_delay {stats?.AecDelayMs} ms");
        }
        if (fault != null) Console.WriteLine($"FAULT: {fault}");

        // 100 frames/s; allow startup slack
        bool flowing = last != null && last.Frames >= (seconds - 1) * 90;
        bool ok = fault == null && flowing && last!.Drops == 0;
        Console.WriteLine(ok ? "OK" : "PROBLEM");
        return ok ? 0 : 1;
    }
}
