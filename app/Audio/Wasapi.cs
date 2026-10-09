using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BetterMic.Audio;

static class Wasapi
{
    const long HnsPerMs = 10_000;

    // Shared mode with Windows doing sample-rate/format conversion to our 48 kHz float, so any
    // device format works without our own resampler.
    public const AudioClientStreamFlags ConvertFlags =
        AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality;

    // Float32 at 48 kHz with the device's own channel count (channel mixing is ours).
    public static WaveFormat Float48k(int channels) => new WaveFormatExtensible(48000, 32, channels);

    public static AudioClient Open(MMDevice device, AudioClientStreamFlags flags, int bufferMs, out int channels)
    {
        var client = device.AudioClient;
        try
        {
            channels = Math.Clamp(client.MixFormat.Channels, 1, 8);
            client.Initialize(AudioClientShareMode.Shared, flags | ConvertFlags, bufferMs * HnsPerMs, 0,
                Float48k(channels), Guid.Empty);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    // Raises the calling thread to the MMCSS "Pro Audio" class so audio keeps up while a game
    // saturates the CPU. Returns a handle for Revert, or zero if MMCSS is unavailable.
    public static IntPtr BoostThread()
    {
        uint taskIndex = 0;
        return AvSetMmThreadCharacteristicsW("Pro Audio", ref taskIndex);
    }

    public static void RevertThread(IntPtr handle)
    {
        if (handle != IntPtr.Zero) AvRevertMmThreadCharacteristics(handle);
    }

    [DllImport("avrt.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr AvSetMmThreadCharacteristicsW(string taskName, ref uint taskIndex);

    [DllImport("avrt.dll")]
    static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);
}
