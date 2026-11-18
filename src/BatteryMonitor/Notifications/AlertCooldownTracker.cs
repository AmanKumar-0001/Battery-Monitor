namespace BatteryMonitor.Notifications;

using System.Collections.Concurrent;
using BatteryMonitor.Core.Models;
using BatteryMonitor.Core.StateMachine;

public sealed class AlertCooldownTracker
{
    private sealed class DeviceAlertState
    {
        public bool LowAlertTriggered { get; set; }
        public DateTimeOffset? LastLowAlertTime { get; set; }
        public bool CriticalAlertTriggered { get; set; }
        public DateTimeOffset? LastCriticalAlertTime { get; set; }

        public void Reset()
        {
            LowAlertTriggered = false;
            LastLowAlertTime = null;
            CriticalAlertTriggered = false;
            LastCriticalAlertTime = null;
        }
    }

    private readonly ConcurrentDictionary<string, DeviceAlertState> _alertStates = new(StringComparer.OrdinalIgnoreCase);

    public AlertSeverity? EvaluateAlert(
        BatteryDevice device,
        int lowThreshold,
        int criticalThreshold,
        TimeSpan cooldown,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(device);

        var state = _alertStates.GetOrAdd(device.Info.DeviceId, _ => new DeviceAlertState());

        // Reset condition: Device is charging or has recovered above low threshold + hysteresis
        if (device.State == BatteryState.Charging ||
            (device.Percentage.HasValue && device.Percentage.Value > lowThreshold + DeviceStateMachine.HysteresisPercentage))
        {
            state.Reset();
            return null;
        }

        if (!device.Percentage.HasValue || device.State == BatteryState.Offline)
        {
            return null;
        }

        int pct = device.Percentage.Value;

        // Check Critical Alert first
        if (pct <= criticalThreshold)
        {
            bool shouldAlert = !state.CriticalAlertTriggered ||
                (state.LastCriticalAlertTime.HasValue && now - state.LastCriticalAlertTime.Value >= cooldown);

            if (shouldAlert)
            {
                state.CriticalAlertTriggered = true;
                state.LastCriticalAlertTime = now;
                return AlertSeverity.Critical;
            }

            return null;
        }

        // Check Low Alert
        if (pct <= lowThreshold)
        {
            bool shouldAlert = !state.LowAlertTriggered ||
                (state.LastLowAlertTime.HasValue && now - state.LastLowAlertTime.Value >= cooldown);

            if (shouldAlert)
            {
                state.LowAlertTriggered = true;
                state.LastLowAlertTime = now;
                return AlertSeverity.Low;
            }

            return null;
        }

        return null;
    }

    public void Reset(string deviceId)
    {
        if (_alertStates.TryGetValue(deviceId, out var state))
        {
            state.Reset();
        }
    }

    public void Clear()
    {
        _alertStates.Clear();
    }
}
