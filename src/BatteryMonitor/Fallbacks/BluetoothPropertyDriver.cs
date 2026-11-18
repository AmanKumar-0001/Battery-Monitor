namespace BatteryMonitor.Fallbacks;

using BatteryMonitor.Core.Models;
using BatteryMonitor.Drivers;
using BatteryMonitor.Interop;

public sealed class BluetoothPropertyDriver : IBatteryDriver
{
    private readonly IBluetoothPropertyReader _propertyReader;

    public string DriverId => "Windows.Bluetooth";
    public int Priority => 100; // Fallback driver

    public BluetoothPropertyDriver(IBluetoothPropertyReader propertyReader)
    {
        _propertyReader = propertyReader ?? throw new ArgumentNullException(nameof(propertyReader));
    }

    public bool Matches(DeviceInfo deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);
        return deviceInfo.Transport == TransportType.Bluetooth;
    }

    public Task<BatteryReading?> TryReadAsync(DeviceInfo deviceInfo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);

        if (!Matches(deviceInfo))
        {
            return Task.FromResult<BatteryReading?>(null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 100% read-only: queries cached Windows Bluetooth battery property
        byte? strength = _propertyReader.ReadBatteryStrength(deviceInfo.DevicePath)
                      ?? _propertyReader.ReadBatteryStrength(deviceInfo.DeviceId);

        if (strength == null)
        {
            return Task.FromResult<BatteryReading?>(null);
        }

        int percentage = Math.Clamp((int)strength.Value, 0, 100);
        var reading = new BatteryReading(percentage, ChargingStatus.Discharging);
        return Task.FromResult<BatteryReading?>(reading);
    }
}
