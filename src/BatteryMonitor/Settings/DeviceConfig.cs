namespace BatteryMonitor.Settings;

public sealed class DeviceConfig
{
    public string? Nickname { get; set; }
    public bool IsHidden { get; set; }
    public int? CustomLowThreshold { get; set; }
    public int? CustomCriticalThreshold { get; set; }
}
