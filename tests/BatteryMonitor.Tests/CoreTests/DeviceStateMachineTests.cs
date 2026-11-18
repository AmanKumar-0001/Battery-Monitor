namespace BatteryMonitor.Tests.CoreTests;

using BatteryMonitor.Core.Models;
using BatteryMonitor.Core.StateMachine;

public class DeviceStateMachineTests
{
    private static BatteryDevice CreateTestDevice()
    {
        var info = new DeviceInfo(
            DeviceId: "DEV_001",
            DevicePath: @"\\?\hid#vid_046d&pid_c547#test",
            VendorId: 0x046D,
            ProductId: 0xC547,
            Name: "Test Mouse",
            Type: DeviceType.Mouse,
            Transport: TransportType.UsbHid
        );
        return new BatteryDevice(info);
    }

    [Fact]
    public void InitialState_IsUnknown()
    {
        var device = CreateTestDevice();
        Assert.Equal(BatteryState.Unknown, device.State);
        Assert.Null(device.Percentage);
        Assert.Equal(0, device.ConsecutiveFailures);
    }

    [Fact]
    public void Transition_UnknownToOnline_WhenDischargingAboveThreshold()
    {
        var device = CreateTestDevice();
        var reading = new BatteryReading(85, ChargingStatus.Discharging);

        var state = DeviceStateMachine.TransitionOnReading(device, reading, lowThreshold: 20);

        Assert.Equal(BatteryState.Online, state);
        Assert.Equal(85, device.Percentage);
        Assert.Equal(0, device.ConsecutiveFailures);
    }

    [Fact]
    public void Transition_UnknownToCharging_WhenStatusIsCharging()
    {
        var device = CreateTestDevice();
        var reading = new BatteryReading(45, ChargingStatus.Charging);

        var state = DeviceStateMachine.TransitionOnReading(device, reading);

        Assert.Equal(BatteryState.Charging, state);
        Assert.Equal(45, device.Percentage);
    }

    [Theory]
    [InlineData(ChargingStatus.Charging)]
    [InlineData(ChargingStatus.Full)]
    [InlineData(ChargingStatus.SlowCharge)]
    public void Transition_ToCharging_OnAllChargingStatuses(ChargingStatus status)
    {
        var device = CreateTestDevice();
        var reading = new BatteryReading(95, status);

        var state = DeviceStateMachine.TransitionOnReading(device, reading);

        Assert.Equal(BatteryState.Charging, state);
    }

    [Fact]
    public void Transition_OnlineToLow_WhenPercentageAtOrBelowThreshold()
    {
        var device = CreateTestDevice();
        DeviceStateMachine.TransitionOnReading(device, new BatteryReading(50, ChargingStatus.Discharging));
        Assert.Equal(BatteryState.Online, device.State);

        var state = DeviceStateMachine.TransitionOnReading(device, new BatteryReading(20, ChargingStatus.Discharging), lowThreshold: 20);
        Assert.Equal(BatteryState.Low, state);

        var stateBelow = DeviceStateMachine.TransitionOnReading(device, new BatteryReading(15, ChargingStatus.Discharging), lowThreshold: 20);
        Assert.Equal(BatteryState.Low, stateBelow);
    }

    [Fact]
    public void Transition_LowToOnline_RequiresHysteresis()
    {
        var device = CreateTestDevice();
        // Enter Low at 18%
        DeviceStateMachine.TransitionOnReading(device, new BatteryReading(18, ChargingStatus.Discharging), lowThreshold: 20);
        Assert.Equal(BatteryState.Low, device.State);

        // At 24% (above threshold 20, but not above 20 + 5 = 25), remains Low
        var state24 = DeviceStateMachine.TransitionOnReading(device, new BatteryReading(24, ChargingStatus.Discharging), lowThreshold: 20);
        Assert.Equal(BatteryState.Low, state24);

        // At 26% (exceeds threshold 20 + hysteresis 5 = 25), returns to Online
        var state26 = DeviceStateMachine.TransitionOnReading(device, new BatteryReading(26, ChargingStatus.Discharging), lowThreshold: 20);
        Assert.Equal(BatteryState.Online, state26);
    }

    [Fact]
    public void Transition_OnlineToAsleep_WhenIsAsleepTrue()
    {
        var device = CreateTestDevice();
        DeviceStateMachine.TransitionOnReading(device, new BatteryReading(80, ChargingStatus.Discharging));
        Assert.Equal(BatteryState.Online, device.State);

        var state = DeviceStateMachine.TransitionOnReading(device, new BatteryReading(80, ChargingStatus.Discharging, IsAsleep: true));
        Assert.Equal(BatteryState.Asleep, state);
        Assert.Equal(80, device.Percentage);
    }

    [Fact]
    public void Transition_AsleepToOnline_OnActiveReading()
    {
        var device = CreateTestDevice();
        DeviceStateMachine.TransitionOnReading(device, new BatteryReading(70, ChargingStatus.Discharging, IsAsleep: true));
        Assert.Equal(BatteryState.Asleep, device.State);

        var state = DeviceStateMachine.TransitionOnReading(device, new BatteryReading(70, ChargingStatus.Discharging, IsAsleep: false));
        Assert.Equal(BatteryState.Online, state);
    }

    [Fact]
    public void Transition_ThreeConsecutiveFailures_TransitionsToOffline()
    {
        var device = CreateTestDevice();
        DeviceStateMachine.TransitionOnReading(device, new BatteryReading(90, ChargingStatus.Discharging));
        Assert.Equal(BatteryState.Online, device.State);

        // Failure 1
        var f1 = DeviceStateMachine.TransitionOnFailure(device);
        Assert.Equal(BatteryState.Online, f1);
        Assert.Equal(1, device.ConsecutiveFailures);

        // Failure 2
        var f2 = DeviceStateMachine.TransitionOnFailure(device);
        Assert.Equal(BatteryState.Online, f2);
        Assert.Equal(2, device.ConsecutiveFailures);

        // Failure 3 -> Offline
        var f3 = DeviceStateMachine.TransitionOnFailure(device);
        Assert.Equal(BatteryState.Offline, f3);
        Assert.Equal(3, device.ConsecutiveFailures);
    }

    [Fact]
    public void Transition_SuccessResetsConsecutiveFailures()
    {
        var device = CreateTestDevice();
        DeviceStateMachine.TransitionOnFailure(device);
        DeviceStateMachine.TransitionOnFailure(device);
        Assert.Equal(2, device.ConsecutiveFailures);

        DeviceStateMachine.TransitionOnReading(device, new BatteryReading(90, ChargingStatus.Discharging));
        Assert.Equal(0, device.ConsecutiveFailures);
        Assert.Equal(BatteryState.Online, device.State);
    }

    [Fact]
    public void Transition_Removal_ImmediatelyOffline()
    {
        var device = CreateTestDevice();
        DeviceStateMachine.TransitionOnReading(device, new BatteryReading(90, ChargingStatus.Discharging));
        Assert.Equal(BatteryState.Online, device.State);

        var state = DeviceStateMachine.TransitionOnRemoval(device);
        Assert.Equal(BatteryState.Offline, state);
    }

    [Fact]
    public void Transition_Arrival_TransitionsOfflineToUnknown()
    {
        var device = CreateTestDevice();
        DeviceStateMachine.TransitionOnRemoval(device);
        Assert.Equal(BatteryState.Offline, device.State);

        var state = DeviceStateMachine.TransitionOnArrival(device);
        Assert.Equal(BatteryState.Unknown, state);
    }

    [Fact]
    public void Transition_SystemResume_TransitionsToUnknownAndResetsFailures()
    {
        var device = CreateTestDevice();
        DeviceStateMachine.TransitionOnReading(device, new BatteryReading(90, ChargingStatus.Discharging));
        DeviceStateMachine.TransitionOnFailure(device);
        Assert.Equal(1, device.ConsecutiveFailures);

        var state = DeviceStateMachine.TransitionOnSystemResume(device);
        Assert.Equal(BatteryState.Unknown, state);
        Assert.Equal(0, device.ConsecutiveFailures);
    }
}
