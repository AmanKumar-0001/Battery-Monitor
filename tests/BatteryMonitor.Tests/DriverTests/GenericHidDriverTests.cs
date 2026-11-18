namespace BatteryMonitor.Tests.DriverTests;

using BatteryMonitor.Core.Models;
using BatteryMonitor.Fallbacks;
using BatteryMonitor.Tests.Mocks;

public class GenericHidDriverTests
{
    private static DeviceInfo CreateGenericDevice()
    {
        return new DeviceInfo(
            DeviceId: "GEN_01",
            DevicePath: @"\\?\hid#vid_045e&pid_028e#test",
            VendorId: 0x045E,
            ProductId: 0x028E,
            Name: "Xbox Wireless Controller",
            Type: DeviceType.Controller,
            Transport: TransportType.UsbHid
        );
    }

    [Fact]
    public void Properties_PriorityAndFallbackBehavior()
    {
        var transport = new MockHidTransport();
        var driver = new GenericHidBatteryDriver(transport);

        Assert.Equal("HID.Generic", driver.DriverId);
        Assert.Equal(100, driver.Priority);
        Assert.True(driver.Matches(CreateGenericDevice()));

        var btDev = new DeviceInfo("BT_DEV", @"\\?\bth#01", 0x045E, 0x02E0, "BT Controller", DeviceType.Controller, TransportType.Bluetooth);
        Assert.False(driver.Matches(btDev));
    }

    [Fact]
    public async Task TryReadAsync_StandardInputReport_ParsesSuccessfully()
    {
        var transport = new MockHidTransport();
        var driver = new GenericHidBatteryDriver(transport);

        // Report ID 0x01, 80% battery
        transport.EnqueueResponse(new byte[] { 0x01, 80 });

        var reading = await driver.TryReadAsync(CreateGenericDevice(), CancellationToken.None);

        Assert.NotNull(reading);
        Assert.Equal(80, reading.Percentage);
        Assert.Equal(ChargingStatus.Discharging, reading.Status);
    }

    [Fact]
    public async Task TryReadAsync_ScaledValueAbove100_ScalesFrom255()
    {
        var transport = new MockHidTransport();
        var driver = new GenericHidBatteryDriver(transport);

        // Raw value 128 (out of 255) -> 50%
        transport.EnqueueResponse(new byte[] { 0x01, 128 });

        var reading = await driver.TryReadAsync(CreateGenericDevice(), CancellationToken.None);

        Assert.NotNull(reading);
        Assert.Equal(50, reading.Percentage);
    }

    [Fact]
    public async Task TryReadAsync_ChargingFlag_ParsesChargingStatus()
    {
        var transport = new MockHidTransport();
        var driver = new GenericHidBatteryDriver(transport);

        // Report ID 0x01, 45% battery, Byte 2 flag 0x01 (Charging)
        transport.EnqueueResponse(new byte[] { 0x01, 45, 0x01 });

        var reading = await driver.TryReadAsync(CreateGenericDevice(), CancellationToken.None);

        Assert.NotNull(reading);
        Assert.Equal(45, reading.Percentage);
        Assert.Equal(ChargingStatus.Charging, reading.Status);
    }

    [Fact]
    public async Task TryReadAsync_FeatureReportFallback_WhenInputReportEmpty()
    {
        var transport = new MockHidTransport
        {
            GetFeatureHandler = buffer =>
            {
                buffer[0] = 0x00;
                buffer[1] = 68; // 68% battery
                return true;
            }
        };
        var driver = new GenericHidBatteryDriver(transport);

        var reading = await driver.TryReadAsync(CreateGenericDevice(), CancellationToken.None);

        Assert.NotNull(reading);
        Assert.Equal(68, reading.Percentage);
    }

    [Fact]
    public async Task TryReadAsync_TruncatedReport_ReturnsNull()
    {
        var transport = new MockHidTransport();
        var driver = new GenericHidBatteryDriver(transport);

        // Only 1 byte returned
        transport.EnqueueResponse(new byte[] { 0x01 });

        var reading = await driver.TryReadAsync(CreateGenericDevice(), CancellationToken.None);

        Assert.Null(reading);
    }
}
