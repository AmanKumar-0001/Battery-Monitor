namespace BatteryMonitor.Notifications;

using BatteryMonitor.Core.Models;
using CommunityToolkit.WinUI.Notifications;

public sealed class ToastNotificationService : INotificationService
{
    public Task ShowBatteryAlertAsync(BatteryDevice device, AlertSeverity severity, int percentage)
    {
        ArgumentNullException.ThrowIfNull(device);

        try
        {
            string title = severity switch
            {
                AlertSeverity.Critical => $"🚨 Critical Battery Warning ({percentage}%)",
                _ => $"⚠️ Low Battery Warning ({percentage}%)"
            };

            string deviceGlyph = device.Info.Type switch
            {
                DeviceType.Mouse => "🖱️",
                DeviceType.Keyboard => "⌨️",
                DeviceType.Headset => "🎧",
                DeviceType.Controller => "🎮",
                _ => "🔋"
            };

            string message = $"{deviceGlyph} {device.DisplayName} has reached {percentage}% remaining. Connect charger soon to avoid interruption.";

            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show();
        }
        catch
        {
            // Graceful fallback if toast notifications are disabled by OS policy or headless session
        }

        return Task.CompletedTask;
    }
}
