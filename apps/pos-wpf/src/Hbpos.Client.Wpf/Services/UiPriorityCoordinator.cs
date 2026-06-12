namespace Hbpos.Client.Wpf.Services;

public interface IUiPriorityCoordinator
{
    bool IsUiActive { get; }

    void NotifyUserInput();

    IDisposable BeginUiOperation(string name);

    Task WaitForUiIdleAsync(CancellationToken cancellationToken = default);
}

public sealed class UiPriorityCoordinator : IUiPriorityCoordinator
{
    public static IUiPriorityCoordinator Noop { get; } = new NoopUiPriorityCoordinator();

    private static readonly TimeSpan DefaultIdleDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DefaultPollDelay = TimeSpan.FromMilliseconds(25);
    private readonly TimeSpan _idleDelay;
    private readonly TimeSpan _pollDelay;
    private readonly object _gate = new();
    private DateTimeOffset _lastUserInputAt = DateTimeOffset.MinValue;
    private int _activeOperationCount;

    public UiPriorityCoordinator()
        : this(DefaultIdleDelay, DefaultPollDelay)
    {
    }

    public UiPriorityCoordinator(TimeSpan idleDelay, TimeSpan pollDelay)
    {
        _idleDelay = idleDelay <= TimeSpan.Zero ? DefaultIdleDelay : idleDelay;
        _pollDelay = pollDelay <= TimeSpan.Zero ? DefaultPollDelay : pollDelay;
    }

    public bool IsUiActive
    {
        get
        {
            lock (_gate)
            {
                return IsUiActiveCore(DateTimeOffset.UtcNow);
            }
        }
    }

    public void NotifyUserInput()
    {
        lock (_gate)
        {
            _lastUserInputAt = DateTimeOffset.UtcNow;
        }
    }

    public IDisposable BeginUiOperation(string name)
    {
        lock (_gate)
        {
            _activeOperationCount++;
            _lastUserInputAt = DateTimeOffset.UtcNow;
        }

        return new UiOperationScope(this);
    }

    public async Task WaitForUiIdleAsync(CancellationToken cancellationToken = default)
    {
        while (IsUiActive)
        {
            await Task.Delay(_pollDelay, cancellationToken);
        }
    }

    private bool IsUiActiveCore(DateTimeOffset now)
    {
        return _activeOperationCount > 0 || now - _lastUserInputAt < _idleDelay;
    }

    private void EndUiOperation()
    {
        lock (_gate)
        {
            if (_activeOperationCount > 0)
            {
                _activeOperationCount--;
            }

            _lastUserInputAt = DateTimeOffset.UtcNow;
        }
    }

    private sealed class UiOperationScope(UiPriorityCoordinator owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.EndUiOperation();
            }
        }
    }

    private sealed class NoopUiPriorityCoordinator : IUiPriorityCoordinator
    {
        public bool IsUiActive => false;

        public void NotifyUserInput()
        {
        }

        public IDisposable BeginUiOperation(string name)
        {
            return NoopDisposable.Instance;
        }

        public Task WaitForUiIdleAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
