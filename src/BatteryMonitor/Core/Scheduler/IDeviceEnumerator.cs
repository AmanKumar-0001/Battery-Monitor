namespace BatteryMonitor.Core.Scheduler;

using BatteryMonitor.Core.Models;

public interface IDeviceEnumerator
{
    IReadOnlyList<DeviceInfo> EnumerateDevices();
}
