namespace BatteryMonitor.Tests.DriverTests;

using BatteryMonitor.Core.Models;
using BatteryMonitor.Fallbacks;
using BatteryMonitor.Interop;

public sealed class MockBluetoothPropertyReader : IBluetoothPropertyReader
{
    public byte? SimulatedStrength { get; set; } = 88;

    public byte? ReadBatteryStrength(string devicePathOrId)
    {
        return SimulatedStrength;
    }
}

public class BluetoothDriverTests
{
    private static DeviceInfo CreateBluetoothDevice()
    {
        return new DeviceInfo(
            DeviceId: "BTH_DEV_01",
            DevicePath: @"\\?\bth#01",
            VendorId: 0x004C,
            ProductId: 0x0267,
            Name: "AirPods Pro",
            Type: DeviceType.Headset,
            Transport: TransportType.Bluetooth
        );
    }

    [Fact]
    public void Properties_PriorityAndFallbackBehavior()
    {
        var reader = new MockBluetoothPropertyReader();
        var driver = new BluetoothPropertyDriver(reader);

        Assert.Equal("Windows.Bluetooth", driver.DriverId);
        Assert.Equal(100, driver.Priority);
        Assert.True(driver.Matches(CreateBluetoothDevice()));

        var usbDev = new DeviceInfo("USB_DEV", @"\\?\hid#01", 0x046D, 0xC547, "USB Mouse", DeviceType.Mouse, TransportType.UsbHid);
        Assert.False(driver.Matches(usbDev));
    }

    [Fact]
    public async Task TryReadAsync_ValidProperty_ReturnsBatteryReading()
    {
        var reader = new MockBluetoothPropertyReader { SimulatedStrength = 75 };
        var driver = new BluetoothPropertyDriver(reader);

        var reading = await driver.TryReadAsync(CreateBluetoothDevice(), CancellationToken.None);

        Assert.NotNull(reading);
        Assert.Equal(75, reading.Percentage);
        Assert.Equal(ChargingStatus.Discharging, reading.Status);
    }

    [Fact]
    public async Task TryReadAsync_PropertyNull_ReturnsNull()
    {
        var reader = new MockBluetoothPropertyReader { SimulatedStrength = null };
        var driver = new BluetoothPropertyDriver(reader);

        var reading = await driver.TryReadAsync(CreateBluetoothDevice(), CancellationToken.None);

        Assert.Null(reading);
    }

    [Fact]
    public async Task TryReadAsync_Cancellation_ThrowsException()
    {
        var reader = new MockBluetoothPropertyReader();
        var driver = new BluetoothPropertyDriver(reader);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await driver.TryReadAsync(CreateBluetoothDevice(), cts.Token);
        });
    }
}
