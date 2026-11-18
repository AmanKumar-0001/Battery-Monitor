namespace BatteryMonitor.Tests.Mocks;

using BatteryMonitor.Core.Scheduler;

public sealed class FakeTimeProvider : ITimeProvider
{
    private DateTimeOffset _utcNow = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly List<TaskCompletionSource<bool>> _delayWaiters = new();
    private readonly object _lock = new();

    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_lock)
            {
                return _utcNow;
            }
        }
        set
        {
            lock (_lock)
            {
                _utcNow = value;
            }
        }
    }

    public int WaiterCount
    {
        get
        {
            lock (_lock)
            {
                return _delayWaiters.Count;
            }
        }
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (cancellationToken.IsCancellationRequested)
        {
            tcs.SetCanceled(cancellationToken);
            return tcs.Task;
        }

        var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        lock (_lock)
        {
            _delayWaiters.Add(tcs);
        }

        return tcs.Task;
    }

    public void Advance(TimeSpan duration)
    {
        List<TaskCompletionSource<bool>> waitersToTrigger;
        lock (_lock)
        {
            _utcNow = _utcNow.Add(duration);
            waitersToTrigger = new List<TaskCompletionSource<bool>>(_delayWaiters);
            _delayWaiters.Clear();
        }

        foreach (var waiter in waitersToTrigger)
        {
            waiter.TrySetResult(true);
        }
    }
}
