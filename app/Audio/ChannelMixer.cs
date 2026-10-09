namespace BetterMic.Audio;

static class ChannelMixer
{
    // Averages interleaved channels into mono. dst must hold src.Length / channels samples.
    public static void Downmix(ReadOnlySpan<float> src, int channels, Span<float> dst)
    {
        if (channels == 1)
        {
            src.CopyTo(dst);
            return;
        }
        int frames = src.Length / channels;
        float scale = 1f / channels;
        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++) sum += src[f * channels + c];
            dst[f] = sum * scale;
        }
    }

    // Copies mono into every channel of an interleaved buffer.
    public static void Upmix(ReadOnlySpan<float> src, int channels, Span<float> dst)
    {
        if (channels == 1)
        {
            src.CopyTo(dst);
            return;
        }
        for (int f = 0; f < src.Length; f++)
        {
            float v = src[f];
            for (int c = 0; c < channels; c++) dst[f * channels + c] = v;
        }
    }
}
