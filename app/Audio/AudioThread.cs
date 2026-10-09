namespace BetterMic.Audio;

// A dedicated, MMCSS-boosted thread running one audio loop. Each loop creates its own COM objects
// on its own thread, so no audio interface crosses apartments. Dispose stops and joins it.
abstract class AudioThread : IDisposable
{
    readonly Thread _thread;
    readonly Action<Exception> _onError;
    protected readonly ManualResetEvent Stop = new(false);

    protected AudioThread(string name, Action<Exception> onError)
    {
        _onError = onError;
        _thread = new Thread(Main) { Name = name, IsBackground = true };
        _thread.SetApartmentState(ApartmentState.MTA);
    }

    protected void StartThread() => _thread.Start();

    protected abstract void Run();

    void Main()
    {
        var mmcss = Wasapi.BoostThread();
        try
        {
            Run();
        }
        catch (Exception e) when (!Stop.WaitOne(0))
        {
            _onError(e);
        }
        catch
        {
            // Errors while shutting down (device already gone) are expected
        }
        finally
        {
            Wasapi.RevertThread(mmcss);
        }
    }

    public void Dispose()
    {
        Stop.Set();
        // A thread stuck in a driver call keeps its handle; leak it rather than free it under the thread
        if (!_thread.IsAlive || _thread.Join(TimeSpan.FromSeconds(2))) Stop.Dispose();
    }
}
