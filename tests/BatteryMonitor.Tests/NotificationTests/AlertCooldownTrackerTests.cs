namespace BatteryMonitor.Tests.NotificationTests;

using BatteryMonitor.Core.Models;
using BatteryMonitor.Notifications;

public class AlertCooldownTrackerTests
{
    private static BatteryDevice CreateDevice(string id = "DEV_ALERT_01")
    {
        var info = new DeviceInfo(id, @"\\?\hid#alert", 0x046D, 0xC547, "Test Mouse", DeviceType.Mouse, TransportType.UsbHid);
        return new BatteryDevice(info);
    }

    [Fact]
    public void LowBatteryThreshold_FiresOnce_CooldownSuppressesSubsequent()
    {
        var tracker = new AlertCooldownTracker();
        var device = CreateDevice();
        device.State = BatteryState.Low;
        device.Percentage = 19; // Below default 20%

        var t0 = DateTimeOffset.UtcNow;
        var cooldown = TimeSpan.FromMinutes(60);

        // 1. First crossing: Must trigger Low alert
        var alert1 = tracker.EvaluateAlert(device, lowThreshold: 20, criticalThreshold: 10, cooldown, t0);
        Assert.Equal(AlertSeverity.Low, alert1);

        // 2. Poll 1 minute later at 18%: Cooldown must suppress duplicate alert
        device.Percentage = 18;
        var alert2 = tracker.EvaluateAlert(device, lowThreshold: 20, criticalThreshold: 10, cooldown, t0.AddMinutes(1));
        Assert.Null(alert2);

        // 3. Poll 61 minutes later: Cooldown elapsed -> Must re-fire alert
        var alert3 = tracker.EvaluateAlert(device, lowThreshold: 20, criticalThreshold: 10, cooldown, t0.AddMinutes(61));
        Assert.Equal(AlertSeverity.Low, alert3);
    }

    [Fact]
    public void CriticalThreshold_FiresCriticalAlert()
    {
        var tracker = new AlertCooldownTracker();
        var device = CreateDevice();
        device.State = BatteryState.Low;
        device.Percentage = 9; // Below critical 10%

        var t0 = DateTimeOffset.UtcNow;
        var cooldown = TimeSpan.FromMinutes(60);

        var alert = tracker.EvaluateAlert(device, lowThreshold: 20, criticalThreshold: 10, cooldown, t0);
        Assert.Equal(AlertSeverity.Critical, alert);
    }

    [Fact]
    public void ChargingState_ResetsAlertsImmediately()
    {
        var tracker = new AlertCooldownTracker();
        var device = CreateDevice();
        device.State = BatteryState.Low;
        device.Percentage = 15;

        var t0 = DateTimeOffset.UtcNow;
        var cooldown = TimeSpan.FromMinutes(60);

        // Trigger alert
        var alert1 = tracker.EvaluateAlert(device, lowThreshold: 20, criticalThreshold: 10, cooldown, t0);
        Assert.Equal(AlertSeverity.Low, alert1);

        // Device plugged in
        device.State = BatteryState.Charging;
        device.Percentage = 16;
        var alertCharging = tracker.EvaluateAlert(device, lowThreshold: 20, criticalThreshold: 10, cooldown, t0.AddMinutes(5));
        Assert.Null(alertCharging);

        // Device unplugged again at 18% (still low): because it was reset by charging, it can alert again!
        device.State = BatteryState.Low;
        device.Percentage = 18;
        var alertAfterUnplug = tracker.EvaluateAlert(device, lowThreshold: 20, criticalThreshold: 10, cooldown, t0.AddMinutes(10));
        Assert.Equal(AlertSeverity.Low, alertAfterUnplug);
    }

    [Fact]
    public void RecoveryHysteresis_ResetsAlertsWhenExceedingThresholdPlus5()
    {
        var tracker = new AlertCooldownTracker();
        var device = CreateDevice();
        device.State = BatteryState.Low;
        device.Percentage = 18;

        var t0 = DateTimeOffset.UtcNow;
        var cooldown = TimeSpan.FromMinutes(60);

        tracker.EvaluateAlert(device, lowThreshold: 20, criticalThreshold: 10, cooldown, t0);

        // Recover to 24% (above 20, but not above 20 + 5 = 25)
        device.Percentage = 24;
        tracker.EvaluateAlert(device, lowThreshold: 20, criticalThreshold: 10, cooldown, t0.AddMinutes(10));

        // Drop back to 19% within cooldown: should still be suppressed because hysteresis wasn't met
        device.Percentage = 19;
        var alertSuppressed = tracker.EvaluateAlert(device, lowThreshold: 20, criticalThreshold: 10, cooldown, t0.AddMinutes(15));
        Assert.Null(alertSuppressed);

        // Now recover to 28% (exceeds 25% hysteresis)
        device.Percentage = 28;
        device.State = BatteryState.Online;
        tracker.EvaluateAlert(device, lowThreshold: 20, criticalThreshold: 10, cooldown, t0.AddMinutes(20));

        // When it drops back to 19% on the next discharge cycle, alert triggers again!
        device.Percentage = 19;
        device.State = BatteryState.Low;
        var alertNewCycle = tracker.EvaluateAlert(device, lowThreshold: 20, criticalThreshold: 10, cooldown, t0.AddMinutes(30));
        Assert.Equal(AlertSeverity.Low, alertNewCycle);
    }
}
