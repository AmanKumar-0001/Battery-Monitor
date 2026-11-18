namespace BatteryMonitor.Tests.CoreTests;

using BatteryMonitor.Core.Scheduler;
using BatteryMonitor.Interop;
using BatteryMonitor.Settings;
using BatteryMonitor.Tests.Mocks;

public class Win32EnumeratorAndOptimizationTests
{
    [Fact]
    public void Win32DeviceEnumerator_WhenDemoModeEnabled_ReturnsDemoDevices()
    {
        var configManager = new ConfigManager();
        configManager.Current.DemoMode = true;

        var enumerator = new Win32DeviceEnumerator(configManager);
        var devices = enumerator.EnumerateDevices();

        Assert.NotEmpty(devices);
        Assert.Contains(devices, d => d.DeviceId == "DEMO_LOGI_PRO_X");
        Assert.Contains(devices, d => d.DeviceId == "DEMO_LOGI_G915");
        Assert.Contains(devices, d => d.DeviceId == "DEMO_XBOX_PAD");
        Assert.Contains(devices, d => d.DeviceId == "DEMO_SONY_WH1000");
    }

    [Fact]
    public void Win32DeviceEnumerator_Default_ReturnsNonEmptyList()
    {
        var configManager = new ConfigManager();
        var enumerator = new Win32DeviceEnumerator(configManager);
        var devices = enumerator.EnumerateDevices();

        // Either finds real physical devices or falls back to demo devices
        Assert.NotNull(devices);
        Assert.NotEmpty(devices);
    }

    [Fact]
    public async Task PollingScheduler_ConcurrentPolls_AreSerializedByPollLock()
    {
        var enumerator = new MockEnumerator();
        enumerator.DeviceList.Add(new BatteryMonitor.Core.Models.DeviceInfo(
            "DEV_CONC",
            @"\\?\hid#conc",
            0x046D,
            0xC547,
            "Concurrent Test Device",
            BatteryMonitor.Core.Models.DeviceType.Mouse,
            BatteryMonitor.Core.Models.TransportType.UsbHid
        ));

        var driver = new MockDriver();
        var configManager = new ConfigManager();
        var scheduler = new PollingScheduler(enumerator, new[] { driver }, configManager);

        // Run 5 simultaneous polls concurrently
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => scheduler.PollAllDevicesAsync(CancellationToken.None))
            .ToArray();

        await Task.WhenAll(tasks);

        // Scheduler should remain intact with 1 device
        Assert.Single(scheduler.Devices);
        Assert.Equal(80, scheduler.Devices["DEV_CONC"].Percentage);

        await scheduler.DisposeAsync();
    }

    [Fact]
    public void TrimWorkingSet_ExecutesCleanlyWithoutException()
    {
        var ex = Record.Exception(() => App.TrimWorkingSet());
        Assert.Null(ex);
    }
}
