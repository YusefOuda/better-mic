namespace BetterMic.Audio;

// The mic and VB-Cable run on different clocks, so the output buffer slowly fills up or drains.
// Instead of dropping or inserting whole frames (audible clicks), each 10 ms frame is stretched
// by one sample when the buffer is outside a tolerance band around the target: a 0.2% speed
// change at most, which is inaudible.
static class DriftCorrector
{
    public static int OutputLength(int bufferedFrames, int targetFrames, int toleranceFrames, int frameLength)
    {
        if (bufferedFrames > targetFrames + toleranceFrames) return frameLength - 1;
        if (bufferedFrames < targetFrames - toleranceFrames) return frameLength + 1;
        return frameLength;
    }

    // Linear-interpolation resample of src into dst. Endpoints map to endpoints, so consecutive
    // frames stay continuous.
    public static void Stretch(ReadOnlySpan<float> src, Span<float> dst)
    {
        if (dst.Length == src.Length)
        {
            src.CopyTo(dst);
            return;
        }
        if (dst.Length == 0 || src.Length == 0) return;
        if (dst.Length == 1)
        {
            dst[0] = src[0];
            return;
        }
        double step = (double)(src.Length - 1) / (dst.Length - 1);
        for (int i = 0; i < dst.Length; i++)
        {
            double pos = i * step;
            int i0 = (int)pos;
            if (i0 >= src.Length - 1)
            {
                dst[i] = src[^1];
                continue;
            }
            float t = (float)(pos - i0);
            dst[i] = src[i0] + (src[i0 + 1] - src[i0]) * t;
        }
    }
}
