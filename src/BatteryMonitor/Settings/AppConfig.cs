namespace BatteryMonitor.Settings;

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public int PollingIntervalSeconds { get; set; } = 60;
    public bool EnableTaskbarWidget { get; set; } = true;
    public bool EnableTrayIcon { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public int LowBatteryThreshold { get; set; } = 20;
    public int CriticalBatteryThreshold { get; set; } = 10;
    public int NotificationCooldownMinutes { get; set; } = 60;
    public bool DemoMode { get; set; } = false;
    public HashSet<string> IgnoreList { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DeviceConfig> Devices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
