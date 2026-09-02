namespace MiniBrowser.App.Services;

public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listener;
    private bool _disposed;

    public SingleInstanceService(string applicationId)
    {
        _mutex = new Mutex(initiallyOwned: true, $"Local\\{applicationId}.Mutex", out var createdNew);
        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            $"Local\\{applicationId}.Activate");
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    public void SignalPrimary()
    {
        if (!IsPrimary)
        {
            _activationEvent.Set();
        }
    }

    public void StartListening(Action activated)
    {
        ArgumentNullException.ThrowIfNull(activated);
        if (!IsPrimary || _listener is not null)
        {
            return;
        }

        _listener = Task.Run(() =>
        {
            var handles = new WaitHandle[] { _activationEvent, _cancellation.Token.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                activated();
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        if (IsPrimary)
        {
            _activationEvent.Set();
        }
        try
        {
            _listener?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Process shutdown must not be blocked by a listener callback failure.
        }

        if (IsPrimary)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex was not owned during a failed startup.
            }
        }

        _activationEvent.Dispose();
        _mutex.Dispose();
        _cancellation.Dispose();
    }
}
