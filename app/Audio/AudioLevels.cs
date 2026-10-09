namespace BetterMic.Audio;

static class AudioLevels
{
    // Samples at or above this are treated as full scale (clipped)
    public const float ClipThreshold = 0.999f;
    // Real clipping flattens several consecutive samples; one stray full-scale sample isn't worth a warning
    public const int ClippedSamplesPerFrame = 3;
    public const double SilenceDb = -100;

    public static float Peak(ReadOnlySpan<float> samples)
    {
        float peak = 0;
        foreach (float s in samples)
        {
            float a = Math.Abs(s);
            if (a > peak) peak = a;
        }
        return peak;
    }

    public static bool IsClipped(ReadOnlySpan<float> samples)
    {
        int count = 0;
        foreach (float s in samples)
            if (Math.Abs(s) >= ClipThreshold && ++count >= ClippedSamplesPerFrame) return true;
        return false;
    }

    public static double ToDb(float amplitude) =>
        amplitude <= 1e-5f ? SilenceDb : 20 * Math.Log10(amplitude);
}
