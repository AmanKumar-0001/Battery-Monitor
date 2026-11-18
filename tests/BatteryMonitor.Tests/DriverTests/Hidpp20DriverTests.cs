namespace BatteryMonitor.Tests.DriverTests;

using BatteryMonitor.Core.Models;
using BatteryMonitor.Drivers;
using BatteryMonitor.Drivers.Allowlist;
using BatteryMonitor.Tests.Mocks;

public class Hidpp20DriverTests
{
    private static DeviceInfo CreateLogitechDevice(string id = "LOGI_01")
    {
        return new DeviceInfo(
            DeviceId: id,
            DevicePath: @"\\?\hid#vid_046d&pid_c547#test",
            VendorId: 0x046D,
            ProductId: 0xC547,
            Name: "Logitech G Pro X Superlight",
            Type: DeviceType.Mouse,
            Transport: TransportType.UsbHid,
            SubDeviceIndex: 0x01
        );
    }

    [Fact]
    public void Matches_LogitechUsbDevice_ReturnsTrue()
    {
        var transport = new MockHidTransport();
        var driver = new Hidpp20Driver(transport);

        Assert.True(driver.Matches(CreateLogitechDevice()));
    }

    [Fact]
    public void Matches_NonLogitechOrBluetooth_ReturnsFalse()
    {
        var transport = new MockHidTransport();
        var driver = new Hidpp20Driver(transport);

        var razerDev = new DeviceInfo("RAZER", @"\\?\hid#razer", 0x1532, 0x0078, "Razer Mouse", DeviceType.Mouse, TransportType.UsbHid);
        Assert.False(driver.Matches(razerDev));

        var logiBt = new DeviceInfo("LOGI_BT", @"\\?\bth#logi", 0x046D, 0xB023, "Logitech BT", DeviceType.Mouse, TransportType.Bluetooth);
        Assert.False(driver.Matches(logiBt));
    }

    [Fact]
    public async Task TryReadAsync_ValidGProXSuperlight_ParsesSuccessfully()
    {
        var transport = new MockHidTransport();
        var driver = new Hidpp20Driver(transport);

        // Discovery response: Feature index 0x04 for 0x1000
        transport.EnqueueResponseHex("1101000004000100000000000000000000000000");
        // Status response: 85% Discharging
        transport.EnqueueResponseHex("1101041055000000000000000000000000000000");

        var reading = await driver.TryReadAsync(CreateLogitechDevice(), CancellationToken.None);

        Assert.NotNull(reading);
        Assert.Equal(85, reading.Percentage);
        Assert.Equal(ChargingStatus.Discharging, reading.Status);
        Assert.False(reading.IsAsleep);
        Assert.Equal(2, transport.WrittenPackets.Count);
    }

    [Fact]
    public async Task TryReadAsync_ValidMXMaster3S_ChargingStatus_ParsesSuccessfully()
    {
        var transport = new MockHidTransport();
        var driver = new Hidpp20Driver(transport);

        // Discovery response: Feature index 0x03 for 0x1000
        transport.EnqueueResponseHex("1101000003000100000000000000000000000000");
        // Status response: 60% Charging (status byte 0x01)
        transport.EnqueueResponseHex("110103103C000100000000000000000000000000");

        var reading = await driver.TryReadAsync(CreateLogitechDevice(), CancellationToken.None);

        Assert.NotNull(reading);
        Assert.Equal(60, reading.Percentage);
        Assert.Equal(ChargingStatus.Charging, reading.Status);
        Assert.False(reading.IsAsleep);
    }

    [Fact]
    public async Task TryReadAsync_ValidG915_FullBattery_ParsesSuccessfully()
    {
        var transport = new MockHidTransport();
        var driver = new Hidpp20Driver(transport);

        // Discovery response: Feature index 0x05 for 0x1000
        transport.EnqueueResponseHex("1101000005000100000000000000000000000000");
        // Status response: 100% Full (status byte 0x02)
        transport.EnqueueResponseHex("1101051064000200000000000000000000000000");

        var reading = await driver.TryReadAsync(CreateLogitechDevice(), CancellationToken.None);

        Assert.NotNull(reading);
        Assert.Equal(100, reading.Percentage);
        Assert.Equal(ChargingStatus.Full, reading.Status);
    }

    [Fact]
    public async Task TryReadAsync_FallbackToFeature1004_When1000NotFound()
    {
        var transport = new MockHidTransport();
        var driver = new Hidpp20Driver(transport);

        // Feature 0x1000 returns index 0x00 (not supported)
        transport.EnqueueResponseHex("1101000000000000000000000000000000000000");
        // Feature 0x1004 returns index 0x07 (supported)
        transport.EnqueueResponseHex("1101000007000100000000000000000000000000");
        // Status query on index 0x07 returns 72% Discharging
        transport.EnqueueResponseHex("1101071048000000000000000000000000000000");

        var reading = await driver.TryReadAsync(CreateLogitechDevice(), CancellationToken.None);

        Assert.NotNull(reading);
        Assert.Equal(72, reading.Percentage);
        Assert.Equal(ChargingStatus.Discharging, reading.Status);
        Assert.Equal(3, transport.WrittenPackets.Count);
    }

    [Fact]
    public async Task TryReadAsync_DeviceAsleepFlag_MarksIsAsleepTrue()
    {
        var transport = new MockHidTransport();
        var driver = new Hidpp20Driver(transport);

        transport.EnqueueResponseHex("1101000004000100000000000000000000000000");
        // Byte 7 has 0x80 (sleep flag)
        transport.EnqueueResponseHex("1101041050000080000000000000000000000000");

        var reading = await driver.TryReadAsync(CreateLogitechDevice(), CancellationToken.None);

        Assert.NotNull(reading);
        Assert.Equal(80, reading.Percentage);
        Assert.True(reading.IsAsleep);
    }

    [Fact]
    public async Task TryReadAsync_ErrorResponse8F_ReturnsNull()
    {
        var transport = new MockHidTransport();
        var driver = new Hidpp20Driver(transport);

        // Discovery returns HID++ error 0x8F (device disconnected or unsupported)
        transport.EnqueueResponseHex("11018F0000000000000000000000000000000000");
        transport.EnqueueResponseHex("11018F0000000000000000000000000000000000");

        var reading = await driver.TryReadAsync(CreateLogitechDevice(), CancellationToken.None);

        Assert.Null(reading);
    }

    [Fact]
    public async Task TryReadAsync_TruncatedPacket_ReturnsNull()
    {
        var transport = new MockHidTransport();
        var driver = new Hidpp20Driver(transport);

        // Discovery response truncated to 3 bytes
        transport.EnqueueResponseHex("110100");

        var reading = await driver.TryReadAsync(CreateLogitechDevice(), CancellationToken.None);

        Assert.Null(reading);
    }

    [Fact]
    public async Task TryReadAsync_Timeout_PropagatesCancellation()
    {
        var transport = new MockHidTransport { SimulateTimeout = true };
        var driver = new Hidpp20Driver(transport);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await driver.TryReadAsync(CreateLogitechDevice(), cts.Token);
        });
    }
}
