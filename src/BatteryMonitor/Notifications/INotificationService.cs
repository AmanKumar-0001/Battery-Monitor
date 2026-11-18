namespace BatteryMonitor.Notifications;

using BatteryMonitor.Core.Models;

public enum AlertSeverity
{
    Low,
    Critical
}

public interface INotificationService
{
    Task ShowBatteryAlertAsync(BatteryDevice device, AlertSeverity severity, int percentage);
}
