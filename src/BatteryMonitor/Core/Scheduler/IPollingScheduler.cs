namespace BatteryMonitor.Core.Scheduler;

using BatteryMonitor.Core.Models;

public interface IPollingScheduler : IAsyncDisposable
{
    IReadOnlyDictionary<string, BatteryDevice> Devices { get; }
    event EventHandler<BatteryDevice>? DeviceUpdated;
    event EventHandler<string>? DeviceRemoved;
    event EventHandler? PollCompleted;

    Task StartAsync(CancellationToken cancellationToken);
    Task TriggerImmediateRefreshAsync(string reason);
    void NotifyDeviceArrival(DeviceInfo deviceInfo);
    void NotifyDeviceRemoval(string deviceId);
    void NotifySystemResume();
    void NotifySystemSuspend();
}
