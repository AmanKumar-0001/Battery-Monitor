namespace BatteryMonitor.Core.Scheduler;

using BatteryMonitor.Core.Models;
using BatteryMonitor.Drivers;

public sealed class DemoDeviceEnumerator : IDeviceEnumerator
{
    public const string DemoMouseId = "DEMO_LOGI_PRO_X";
    public const string DemoKeyboardId = "DEMO_LOGI_G915";
    public const string DemoControllerId = "DEMO_XBOX_PAD";
    public const string DemoHeadsetId = "DEMO_SONY_WH1000";

    public IReadOnlyList<DeviceInfo> EnumerateDevices()
    {
        return new List<DeviceInfo>
        {
            new DeviceInfo(DemoMouseId, @"\\?\demo#mouse", 0x046D, 0xC547, "Logitech G Pro X Superlight", DeviceType.Mouse, TransportType.UsbHid),
            new DeviceInfo(DemoKeyboardId, @"\\?\demo#keyboard", 0x046D, 0xC53A, "Logitech G915 TKL", DeviceType.Keyboard, TransportType.UsbHid),
            new DeviceInfo(DemoControllerId, @"\\?\demo#controller", 0x045E, 0x028E, "Xbox Wireless Controller", DeviceType.Controller, TransportType.UsbHid),
            new DeviceInfo(DemoHeadsetId, @"\\?\demo#headset", 0x054C, 0x0CD3, "Sony WH-1000XM4", DeviceType.Headset, TransportType.Bluetooth)
        };
    }
}

public sealed class DemoBatteryDriver : IBatteryDriver
{
    public string DriverId => "Demo.Simulated";
    public int Priority => 1; // Highest priority for demo devices

    private static readonly Dictionary<string, BatteryReading> DemoReadings = new(StringComparer.OrdinalIgnoreCase)
    {
        [DemoDeviceEnumerator.DemoMouseId] = new BatteryReading(85, ChargingStatus.Discharging),
        [DemoDeviceEnumerator.DemoKeyboardId] = new BatteryReading(60, ChargingStatus.Charging),
        [DemoDeviceEnumerator.DemoControllerId] = new BatteryReading(42, ChargingStatus.Discharging),
        [DemoDeviceEnumerator.DemoHeadsetId] = new BatteryReading(15, ChargingStatus.Discharging)
    };

    public bool Matches(DeviceInfo deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);
        return deviceInfo.DeviceId.StartsWith("DEMO_", StringComparison.OrdinalIgnoreCase);
    }

    public Task<BatteryReading?> TryReadAsync(DeviceInfo deviceInfo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);
        if (DemoReadings.TryGetValue(deviceInfo.DeviceId, out var reading))
        {
            return Task.FromResult<BatteryReading?>(reading);
        }
        return Task.FromResult<BatteryReading?>(new BatteryReading(50, ChargingStatus.Discharging));
    }
}
