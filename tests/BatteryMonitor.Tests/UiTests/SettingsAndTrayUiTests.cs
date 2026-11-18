namespace BatteryMonitor.Tests.UiTests;

using System.Threading;
using System.Windows.Controls;
using BatteryMonitor.Core.Models;
using BatteryMonitor.Core.Scheduler;
using BatteryMonitor.Settings;
using BatteryMonitor.Tests.Mocks;
using BatteryMonitor.Ui.Tray;

public class SettingsAndTrayUiTests : IDisposable
{
    private readonly string _testBaseDir;
    private readonly string _testAppDataDir;
    private readonly ConfigManager _configManager;
    private readonly MockEnumerator _enumerator;
    private readonly MockDriver _driver;
    private readonly FakeTimeProvider _timeProvider;

    public SettingsAndTrayUiTests()
    {
        _testBaseDir = Path.Combine(Path.GetTempPath(), "BM_UiTest_Base_" + Guid.NewGuid().ToString("N"));
        _testAppDataDir = Path.Combine(Path.GetTempPath(), "BM_UiTest_AppData_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testBaseDir);
        Directory.CreateDirectory(_testAppDataDir);

        _configManager = new ConfigManager(_testBaseDir, _testAppDataDir);
        _configManager.Load();

        _enumerator = new MockEnumerator();
        _driver = new MockDriver();
        _timeProvider = new FakeTimeProvider();
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

    private static void RunInSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            throw new AggregateException(ex);
        }
    }

    [Fact]
    public async Task SettingsViewModel_LoadsDevicesAndSavesEdits()
    {
        var devInfo = new DeviceInfo("DEV_01", @"\\?\hid#01", 0x046D, 0xC547, "Test Mouse", DeviceType.Mouse, TransportType.UsbHid);
        _enumerator.DeviceList.Add(devInfo);

        await using var scheduler = new PollingScheduler(_enumerator, new[] { _driver }, _configManager, _timeProvider);
        using var cts = new CancellationTokenSource();
        await scheduler.StartAsync(cts.Token);
        await Task.Delay(50);

        var vm = new SettingsViewModel(_configManager, scheduler);
        Assert.Single(vm.DeviceItems);

        var item = vm.DeviceItems[0];
        Assert.Equal("DEV_01", item.DeviceId);
        Assert.Equal("Test Mouse", item.Nickname);
        Assert.False(item.IsHidden);

        // Edit nickname and visibility
        item.Nickname = "Custom Superlight";
        item.IsHidden = true;

        // Config and tracked device should reflect updates
        Assert.Equal("Custom Superlight", _configManager.Current.Devices["DEV_01"].Nickname);
        Assert.True(_configManager.Current.Devices["DEV_01"].IsHidden);
        Assert.Equal("Custom Superlight", scheduler.Devices["DEV_01"].DisplayName);
        Assert.True(scheduler.Devices["DEV_01"].IsHidden);

        cts.Cancel();
    }

    [Fact]
    public void SettingsViewModel_ClampsThresholdsAndInterval()
    {
        var scheduler = new PollingScheduler(_enumerator, new[] { _driver }, _configManager, _timeProvider);
        var vm = new SettingsViewModel(_configManager, scheduler);

        // Clamping Low Battery: 5..50
        vm.LowBatteryThreshold = 2;
        Assert.Equal(5, vm.LowBatteryThreshold);

        vm.LowBatteryThreshold = 80;
        Assert.Equal(50, vm.LowBatteryThreshold);

        // Clamping Critical Battery: 1..20
        vm.CriticalBatteryThreshold = 0;
        Assert.Equal(1, vm.CriticalBatteryThreshold);

        vm.CriticalBatteryThreshold = 35;
        Assert.Equal(20, vm.CriticalBatteryThreshold);

        // Clamping Polling Interval: 10..300
        vm.PollingIntervalSeconds = 5;
        Assert.Equal(10, vm.PollingIntervalSeconds);

        vm.PollingIntervalSeconds = 600;
        Assert.Equal(300, vm.PollingIntervalSeconds);
    }

    [Fact]
    public async Task TrayIconManager_BuildContextMenu_ContainsRequiredItems()
    {
        var devInfo = new DeviceInfo("DEV_TRAY", @"\\?\hid#tray", 0x046D, 0xC547, "G Pro Mouse", DeviceType.Mouse, TransportType.UsbHid);
        _enumerator.DeviceList.Add(devInfo);

        await using var scheduler = new PollingScheduler(_enumerator, new[] { _driver }, _configManager, _timeProvider);
        using var cts = new CancellationTokenSource();
        await scheduler.StartAsync(cts.Token);
        await Task.Delay(50);

        bool settingsOpened = false;
        bool exited = false;

        RunInSta(() =>
        {
            using var tray = new TrayIconManager(
                scheduler,
                _configManager,
                () => settingsOpened = true,
                () => exited = true
            );

            var menu = tray.BuildContextMenu();
            Assert.NotNull(menu);

            // Look for expected headers
            var items = menu.Items.OfType<MenuItem>().ToList();
            Assert.Contains(items, i => i.Header.ToString() == "Battery Monitor");
            Assert.Contains(items, i => i.Header.ToString()!.Contains("G Pro Mouse"));
            Assert.Contains(items, i => i.Header.ToString()!.Contains("Refresh Now"));
            Assert.Contains(items, i => i.Header.ToString()!.Contains("Start with Windows"));
            Assert.Contains(items, i => i.Header.ToString()!.Contains("Settings..."));
            Assert.Contains(items, i => i.Header.ToString()!.Contains("Exit"));

            var settingsMenuItem = items.First(i => i.Header.ToString()!.Contains("Settings..."));
            settingsMenuItem.RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(settingsOpened);

            var exitMenuItem = items.First(i => i.Header.ToString()!.Contains("Exit"));
            exitMenuItem.RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(exited);
        });

        cts.Cancel();
    }
}
