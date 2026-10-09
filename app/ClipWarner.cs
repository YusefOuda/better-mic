namespace BetterMic;

// Decides when to warn that the raw mic is clipping. Fed the pipeline's cumulative clipped-frame
// count about once a second; warns when enough clipped frames pile up within a short window, and
// then stays quiet for a while so a loud evening doesn't turn into a notification storm.
sealed class ClipWarner
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(30);
    public const int FramesToWarn = 5; // 50 ms of clipped audio

    readonly Queue<(DateTime Time, long Count)> _history = new();
    DateTime? _lastWarning;

    public bool Update(long clippedFrames, DateTime now)
    {
        // The counter starts over whenever the audio pipeline restarts
        if (_history.Count > 0 && clippedFrames < _history.Last().Count) _history.Clear();
        _history.Enqueue((now, clippedFrames));
        while (_history.Count > 1 && now - _history.Peek().Time > Window) _history.Dequeue();

        long recent = clippedFrames - _history.Peek().Count;
        if (recent < FramesToWarn) return false;
        if (_lastWarning is { } last && now - last < Cooldown) return false;
        _lastWarning = now;
        return true;
    }
}
