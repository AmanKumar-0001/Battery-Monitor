namespace BatteryMonitor.Core.Models;

public sealed class BatteryDevice
{
    public DeviceInfo Info { get; }
    public string DisplayName { get; set; }
    public BatteryState State { get; internal set; } = BatteryState.Unknown;
    public int? Percentage { get; internal set; }
    public ChargingStatus ChargingStatus { get; internal set; } = ChargingStatus.Discharging;
    public double? Voltage { get; internal set; }
    public DateTimeOffset LastSeen { get; internal set; }
    public bool IsHidden { get; set; }
    public int ConsecutiveFailures { get; internal set; }

    public BatteryDevice(DeviceInfo info, string? nickname = null)
    {
        Info = info ?? throw new ArgumentNullException(nameof(info));
        DisplayName = string.IsNullOrWhiteSpace(nickname) ? info.Name : nickname;
        LastSeen = DateTimeOffset.UtcNow;
    }
}
