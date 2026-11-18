namespace BatteryMonitor.Core.StateMachine;

using BatteryMonitor.Core.Models;

public static class DeviceStateMachine
{
    public const int DefaultLowThreshold = 20;
    public const int DefaultCriticalThreshold = 10;
    public const int HysteresisPercentage = 5;
    public const int MaxConsecutiveFailures = 3;

    /// <summary>
    /// Evaluates a new battery reading and transitions the device state.
    /// </summary>
    public static BatteryState TransitionOnReading(
        BatteryDevice device,
        BatteryReading reading,
        int lowThreshold = DefaultLowThreshold)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(reading);

        device.ConsecutiveFailures = 0;
        device.LastSeen = reading.Timestamp;
        device.Percentage = reading.Percentage;
        device.ChargingStatus = reading.Status;
        device.Voltage = reading.Voltage;

        BatteryState nextState;

        if (reading.Status is ChargingStatus.Charging or ChargingStatus.Full or ChargingStatus.SlowCharge)
        {
            nextState = BatteryState.Charging;
        }
        else if (reading.IsAsleep)
        {
            nextState = BatteryState.Asleep;
        }
        else
        {
            // Discharging or Invalid
            if (device.State == BatteryState.Low)
            {
                // Must exceed threshold + hysteresis to transition back to Online
                if (reading.Percentage > lowThreshold + HysteresisPercentage)
                {
                    nextState = BatteryState.Online;
                }
                else
                {
                    nextState = BatteryState.Low;
                }
            }
            else
            {
                if (reading.Percentage <= lowThreshold)
                {
                    nextState = BatteryState.Low;
                }
                else
                {
                    nextState = BatteryState.Online;
                }
            }
        }

        device.State = nextState;
        return nextState;
    }

    /// <summary>
    /// Records a polling failure or timeout. Transitions to Offline after MaxConsecutiveFailures (3).
    /// </summary>
    public static BatteryState TransitionOnFailure(BatteryDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        device.ConsecutiveFailures++;
        if (device.ConsecutiveFailures >= MaxConsecutiveFailures)
        {
            device.State = BatteryState.Offline;
        }

        return device.State;
    }

    /// <summary>
    /// Explicitly transitions device to Offline when hardware node removal is detected.
    /// </summary>
    public static BatteryState TransitionOnRemoval(BatteryDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        device.State = BatteryState.Offline;
        return device.State;
    }

    /// <summary>
    /// Transitions device from Offline to Unknown when device arrival is detected.
    /// </summary>
    public static BatteryState TransitionOnArrival(BatteryDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.State == BatteryState.Offline)
        {
            device.State = BatteryState.Unknown;
        }
        return device.State;
    }

    /// <summary>
    /// Transitions device to Unknown on system resume from sleep to await a fresh poll.
    /// </summary>
    public static BatteryState TransitionOnSystemResume(BatteryDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        device.State = BatteryState.Unknown;
        device.ConsecutiveFailures = 0;
        return device.State;
    }
}
