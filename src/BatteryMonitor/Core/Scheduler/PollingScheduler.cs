namespace BatteryMonitor.Core.Scheduler;

using System.Collections.Concurrent;
using BatteryMonitor.Core.Models;
using BatteryMonitor.Core.StateMachine;
using BatteryMonitor.Drivers;
using BatteryMonitor.Settings;

public sealed class PollingScheduler : IPollingScheduler
{
    private readonly IDeviceEnumerator _enumerator;
    private readonly IReadOnlyList<IBatteryDriver> _drivers;
    private readonly ConfigManager _configManager;
    private readonly ITimeProvider _timeProvider;
    private readonly TimeSpan _deviceTimeout;
    private readonly ConcurrentDictionary<string, BatteryDevice> _devices = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _runCts;
    private TaskCompletionSource<bool> _refreshSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _refreshLock = new();
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private Task? _schedulerTask;
    private bool _isDisposed;

    public IReadOnlyDictionary<string, BatteryDevice> Devices => _devices;

    public event EventHandler<BatteryDevice>? DeviceUpdated;
    public event EventHandler<string>? DeviceRemoved;
    public event EventHandler? PollCompleted;

    public PollingScheduler(
        IDeviceEnumerator enumerator,
        IEnumerable<IBatteryDriver> drivers,
        ConfigManager configManager,
        ITimeProvider? timeProvider = null,
        TimeSpan? deviceTimeout = null)
    {
        _enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
        _drivers = (drivers ?? throw new ArgumentNullException(nameof(drivers)))
            .OrderBy(d => d.Priority)
            .ToList();
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _timeProvider = timeProvider ?? new SystemTimeProvider();
        _deviceTimeout = deviceTimeout ?? TimeSpan.FromSeconds(2);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _schedulerTask = Task.Run(() => RunPollingLoopAsync(_runCts.Token), _runCts.Token);
        return Task.CompletedTask;
    }

    public Task TriggerImmediateRefreshAsync(string reason)
    {
        lock (_refreshLock)
        {
            _refreshSignal.TrySetResult(true);
        }
        return Task.CompletedTask;
    }

    public void NotifyDeviceArrival(DeviceInfo deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);

        if (_configManager.Current.IgnoreList.Contains(deviceInfo.DeviceId))
        {
            return;
        }

        var device = _devices.GetOrAdd(deviceInfo.DeviceId, id =>
        {
            string? nickname = null;
            if (_configManager.Current.Devices.TryGetValue(id, out var devCfg))
            {
                nickname = devCfg.Nickname;
            }
            return new BatteryDevice(deviceInfo, nickname);
        });

        DeviceStateMachine.TransitionOnArrival(device);
        DeviceUpdated?.Invoke(this, device);
        TriggerImmediateRefreshAsync("DeviceArrival");
    }

    public void NotifyDeviceRemoval(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        if (_devices.TryGetValue(deviceId, out var device))
        {
            DeviceStateMachine.TransitionOnRemoval(device);
            DeviceUpdated?.Invoke(this, device);
            DeviceRemoved?.Invoke(this, deviceId);
        }

        TriggerImmediateRefreshAsync("DeviceRemoval");
    }

    public void NotifySystemResume()
    {
        foreach (var device in _devices.Values)
        {
            DeviceStateMachine.TransitionOnSystemResume(device);
            DeviceUpdated?.Invoke(this, device);
        }

        // Delay 2s for transceivers to stabilize, then trigger refresh
        Task.Run(async () =>
        {
            try
            {
                await _timeProvider.DelayAsync(TimeSpan.FromSeconds(2), _runCts?.Token ?? CancellationToken.None);
                await TriggerImmediateRefreshAsync("SystemResume");
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    public void NotifySystemSuspend()
    {
        foreach (var device in _devices.Values)
        {
            device.State = BatteryState.Unknown;
            DeviceUpdated?.Invoke(this, device);
        }
    }

    private async Task RunPollingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await PollAllDevicesAsync(cancellationToken);
            PollCompleted?.Invoke(this, EventArgs.Empty);

            int intervalSeconds = _configManager.Current.PollingIntervalSeconds;
            if (intervalSeconds <= 0)
            {
                intervalSeconds = 60;
            }

            TaskCompletionSource<bool> currentSignal;
            lock (_refreshLock)
            {
                _refreshSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                currentSignal = _refreshSignal;
            }

            try
            {
                var delayTask = _timeProvider.DelayAsync(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
                var completedTask = await Task.WhenAny(delayTask, currentSignal.Task);
                if (completedTask == delayTask)
                {
                    await delayTask; // propagate any cancellation
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task PollAllDevicesAsync(CancellationToken cancellationToken)
    {
        await _pollLock.WaitAsync(cancellationToken);
        try
        {
            var discovered = _enumerator.EnumerateDevices();
            var config = _configManager.Current;

            // Sync discovered devices
            var activeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in discovered)
            {
                if (config.IgnoreList.Contains(info.DeviceId))
                {
                    continue;
                }

                activeIds.Add(info.DeviceId);

                var device = _devices.GetOrAdd(info.DeviceId, id =>
                {
                    string? nickname = null;
                    bool isHidden = false;
                    if (config.Devices.TryGetValue(id, out var devCfg))
                    {
                        nickname = devCfg.Nickname;
                        isHidden = devCfg.IsHidden;
                    }
                    var d = new BatteryDevice(info, nickname)
                    {
                        IsHidden = isHidden
                    };
                    return d;
                });

                // Refresh settings from config
                if (config.Devices.TryGetValue(info.DeviceId, out var deviceConfig))
                {
                    if (!string.IsNullOrWhiteSpace(deviceConfig.Nickname))
                    {
                        device.DisplayName = deviceConfig.Nickname;
                    }
                    device.IsHidden = deviceConfig.IsHidden;
                }
            }

            // Detect devices that disappeared from enumeration
            foreach (var (id, device) in _devices)
            {
                if (!activeIds.Contains(id) && device.State != BatteryState.Offline)
                {
                    DeviceStateMachine.TransitionOnRemoval(device);
                    DeviceUpdated?.Invoke(this, device);
                }
            }

            // Query each active device in isolation with a timeout
            foreach (var device in _devices.Values)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (device.State == BatteryState.Offline)
                {
                    continue;
                }

                if (config.IgnoreList.Contains(device.Info.DeviceId))
                {
                    continue;
                }

                int lowThreshold = config.LowBatteryThreshold;
                if (config.Devices.TryGetValue(device.Info.DeviceId, out var devCfg) && devCfg.CustomLowThreshold.HasValue)
                {
                    lowThreshold = devCfg.CustomLowThreshold.Value;
                }

                // Find matching driver
                var driver = _drivers.FirstOrDefault(d => d.Matches(device.Info));
                if (driver == null)
                {
                    continue;
                }

                // Isolated device query with individual timeout
                using var timeoutCts = new CancellationTokenSource(_deviceTimeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                try
                {
                    var reading = await driver.TryReadAsync(device.Info, linkedCts.Token);
                    if (reading != null)
                    {
                        DeviceStateMachine.TransitionOnReading(device, reading, lowThreshold);
                    }
                    else
                    {
                        DeviceStateMachine.TransitionOnFailure(device);
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    // Timeout on this single device
                    DeviceStateMachine.TransitionOnFailure(device);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Isolated per-device error handling
                    DeviceStateMachine.TransitionOnFailure(device);
                }

                DeviceUpdated?.Invoke(this, device);
            }
        }
        finally
        {
            _pollLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _runCts?.Cancel();

        lock (_refreshLock)
        {
            _refreshSignal.TrySetCanceled();
        }

        if (_schedulerTask != null)
        {
            try
            {
                await _schedulerTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _runCts?.Dispose();
        _pollLock.Dispose();
    }
}
