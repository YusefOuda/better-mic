namespace BetterMic.Audio;

// Records the raw and processed mic side by side for the "Test microphone" window. The mic thread
// writes; the UI reads the buffers once IsComplete is true.
sealed class MicRecorder
{
    readonly float[] _raw;
    readonly float[] _processed;
    int _position;

    public MicRecorder(int samples)
    {
        _raw = new float[samples];
        _processed = new float[samples];
    }

    public bool IsComplete => Volatile.Read(ref _position) >= _raw.Length;
    public double Progress => (double)Volatile.Read(ref _position) / _raw.Length;

    // Valid once IsComplete
    public float[] Raw => _raw;
    public float[] Processed => _processed;

    // Mic thread only
    public void Write(ReadOnlySpan<float> raw, ReadOnlySpan<float> processed)
    {
        int pos = _position;
        int n = Math.Min(raw.Length, _raw.Length - pos);
        if (n <= 0) return;
        raw[..n].CopyTo(_raw.AsSpan(pos));
        processed[..n].CopyTo(_processed.AsSpan(pos));
        Volatile.Write(ref _position, pos + n);
    }
}
