namespace BatteryMonitor.Tests.UiTests;

using BatteryMonitor.Core.Models;
using BatteryMonitor.Core.Scheduler;
using BatteryMonitor.Settings;
using BatteryMonitor.Tests.Mocks;

public class DemoModeTests : IDisposable
{
    private readonly string _testBaseDir;
    private readonly string _testAppDataDir;
    private readonly ConfigManager _configManager;

    public DemoModeTests()
    {
        _testBaseDir = Path.Combine(Path.GetTempPath(), "BM_DemoTest_Base_" + Guid.NewGuid().ToString("N"));
        _testAppDataDir = Path.Combine(Path.GetTempPath(), "BM_DemoTest_AppData_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testBaseDir);
        Directory.CreateDirectory(_testAppDataDir);

        _configManager = new ConfigManager(_testBaseDir, _testAppDataDir);
        _configManager.Load();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testBaseDir)) Directory.Delete(_testBaseDir, true);
            if (Directory.Exists(_testAppDataDir)) Directory.Delete(_testAppDataDir, true);
        }
        catch
        {
        }
    }

    [Fact]
    public void DemoEnumerator_ReturnsExpectedFourDevices()
    {
        var enumerator = new DemoDeviceEnumerator();
        var devices = enumerator.EnumerateDevices();

        Assert.Equal(4, devices.Count);
        Assert.Contains(devices, d => d.DeviceId == DemoDeviceEnumerator.DemoMouseId && d.Type == DeviceType.Mouse);
        Assert.Contains(devices, d => d.DeviceId == DemoDeviceEnumerator.DemoKeyboardId && d.Type == DeviceType.Keyboard);
        Assert.Contains(devices, d => d.DeviceId == DemoDeviceEnumerator.DemoControllerId && d.Type == DeviceType.Controller);
        Assert.Contains(devices, d => d.DeviceId == DemoDeviceEnumerator.DemoHeadsetId && d.Type == DeviceType.Headset);
    }

    [Fact]
    public async Task DemoDriver_ReturnsExpectedBatteryReadings()
    {
        var driver = new DemoBatteryDriver();
        var mouse = new DeviceInfo(DemoDeviceEnumerator.DemoMouseId, @"\\?\demo#mouse", 0x046D, 0xC547, "Mouse", DeviceType.Mouse, TransportType.UsbHid);
        var kbd = new DeviceInfo(DemoDeviceEnumerator.DemoKeyboardId, @"\\?\demo#kbd", 0x046D, 0xC53A, "Keyboard", DeviceType.Keyboard, TransportType.UsbHid);

        Assert.True(driver.Matches(mouse));
        Assert.True(driver.Matches(kbd));

        var mouseReading = await driver.TryReadAsync(mouse, CancellationToken.None);
        var kbdReading = await driver.TryReadAsync(kbd, CancellationToken.None);

        Assert.NotNull(mouseReading);
        Assert.Equal(85, mouseReading.Percentage);
        Assert.Equal(ChargingStatus.Discharging, mouseReading.Status);

        Assert.NotNull(kbdReading);
        Assert.Equal(60, kbdReading.Percentage);
        Assert.Equal(ChargingStatus.Charging, kbdReading.Status);
    }

    [Fact]
    public async Task DemoMode_SchedulerIntegration_PopulatesAndPollsDemoDevices()
    {
        var enumerator = new DemoDeviceEnumerator();
        var driver = new DemoBatteryDriver();
        var timeProvider = new FakeTimeProvider();

        await using var scheduler = new PollingScheduler(
            enumerator,
            new[] { driver },
            _configManager,
            timeProvider
        );

        using var cts = new CancellationTokenSource();
        await scheduler.StartAsync(cts.Token);
        await Task.Delay(80);

        Assert.Equal(4, scheduler.Devices.Count);

        var mouse = scheduler.Devices[DemoDeviceEnumerator.DemoMouseId];
        Assert.Equal(BatteryState.Online, mouse.State);
        Assert.Equal(85, mouse.Percentage);

        var kbd = scheduler.Devices[DemoDeviceEnumerator.DemoKeyboardId];
        Assert.Equal(BatteryState.Charging, kbd.State);
        Assert.Equal(60, kbd.Percentage);

        var headset = scheduler.Devices[DemoDeviceEnumerator.DemoHeadsetId];
        Assert.Equal(BatteryState.Low, headset.State); // 15% <= default low threshold 20%
        Assert.Equal(15, headset.Percentage);

        cts.Cancel();
    }
}
