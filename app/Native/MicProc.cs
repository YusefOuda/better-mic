using System.Runtime.InteropServices;

namespace BetterMic.Native;

// Mirrors native/micproc.h. Keep the structs and enum values in sync with it.
public enum NoiseMode
{
    Off = 0,
    WebRtc = 1,
    RNNoise = 2,
    DeepFilter = 3,
}

[StructLayout(LayoutKind.Sequential)]
struct MicProcConfig
{
    public int AecEnabled;
    public int NsMode;
    public int NsLevel;
    public int AgcEnabled;
    public int HpfEnabled;
}

[StructLayout(LayoutKind.Sequential)]
struct MicProcStats
{
    public float SpeechProbability;
    public int AecDelayMs;
    public float AecErleDb;
    public float NsSnrDb;
}

static partial class MicProcNative
{
    const string Dll = "micproc.dll";
    public const int ApiVersion = 1;
    public const int SampleRate = 48000;
    public const int FrameSize = 480;

    [LibraryImport(Dll, EntryPoint = "micproc_api_version")]
    public static partial int ApiVersionOf();

    [LibraryImport(Dll, EntryPoint = "micproc_create")]
    public static partial IntPtr Create(in MicProcConfig config);

    [LibraryImport(Dll, EntryPoint = "micproc_destroy")]
    public static partial void Destroy(IntPtr p);

    [LibraryImport(Dll, EntryPoint = "micproc_set_config")]
    public static partial int SetConfig(IntPtr p, in MicProcConfig config);

    [LibraryImport(Dll, EntryPoint = "micproc_process_render")]
    public static unsafe partial int ProcessRender(IntPtr p, float* frame, int channels);

    [LibraryImport(Dll, EntryPoint = "micproc_process_capture")]
    public static unsafe partial int ProcessCapture(IntPtr p, float* frame);

    [LibraryImport(Dll, EntryPoint = "micproc_set_stream_delay_ms")]
    public static partial int SetStreamDelayMs(IntPtr p, int delayMs);

    [LibraryImport(Dll, EntryPoint = "micproc_get_stats")]
    public static partial int GetStats(IntPtr p, out MicProcStats stats);
}

// Owns a native micproc instance. Process* calls are safe from the audio threads as documented
// in micproc.h (render and capture on separate threads; config from any thread).
sealed class MicProcessor : IDisposable
{
    IntPtr _handle;

    public MicProcessor(MicProcConfig config)
    {
        int version = MicProcNative.ApiVersionOf();
        if (version != MicProcNative.ApiVersion)
            throw new InvalidOperationException($"micproc.dll API version {version}, expected {MicProcNative.ApiVersion}");
        _handle = MicProcNative.Create(config);
        if (_handle == IntPtr.Zero) throw new InvalidOperationException("micproc_create failed");
    }

    public void SetConfig(MicProcConfig config)
    {
        int err = MicProcNative.SetConfig(Handle, config);
        if (err != 0) throw new InvalidOperationException($"micproc_set_config failed ({err})");
    }

    public unsafe int ProcessRender(ReadOnlySpan<float> interleaved, int channels)
    {
        fixed (float* p = interleaved) return MicProcNative.ProcessRender(Handle, p, channels);
    }

    public unsafe int ProcessCapture(Span<float> frame)
    {
        fixed (float* p = frame) return MicProcNative.ProcessCapture(Handle, p);
    }

    public void SetStreamDelayMs(int delayMs) => MicProcNative.SetStreamDelayMs(Handle, delayMs);

    public MicProcStats GetStats()
    {
        MicProcNative.GetStats(Handle, out var stats);
        return stats;
    }

    IntPtr Handle => _handle != IntPtr.Zero ? _handle : throw new ObjectDisposedException(nameof(MicProcessor));

    public void Dispose()
    {
        var h = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (h != IntPtr.Zero) MicProcNative.Destroy(h);
    }
}
