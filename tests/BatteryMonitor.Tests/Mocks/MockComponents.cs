namespace BatteryMonitor.Tests.Mocks;

using BatteryMonitor.Core.Models;
using BatteryMonitor.Core.Scheduler;
using BatteryMonitor.Drivers;

public sealed class MockEnumerator : IDeviceEnumerator
{
    public List<DeviceInfo> DeviceList { get; } = new();

    public IReadOnlyList<DeviceInfo> EnumerateDevices()
    {
        return DeviceList.ToList();
    }
}

public sealed class MockDriver : IBatteryDriver
{
    public string DriverId { get; init; } = "Mock.Driver";
    public int Priority { get; init; } = 10;
    public Func<DeviceInfo, bool> MatchPredicate { get; set; } = _ => true;
    public Func<DeviceInfo, CancellationToken, Task<BatteryReading?>> ReadHandler { get; set; } =
        (_, _) => Task.FromResult<BatteryReading?>(new BatteryReading(80, ChargingStatus.Discharging));

    public bool Matches(DeviceInfo deviceInfo) => MatchPredicate(deviceInfo);

    public Task<BatteryReading?> TryReadAsync(DeviceInfo deviceInfo, CancellationToken cancellationToken)
    {
        return ReadHandler(deviceInfo, cancellationToken);
    }
}
