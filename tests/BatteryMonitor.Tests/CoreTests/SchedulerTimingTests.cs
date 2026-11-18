namespace BatteryMonitor.Tests.CoreTests;

using BatteryMonitor.Core.Models;
using BatteryMonitor.Core.Scheduler;
using BatteryMonitor.Drivers;
using BatteryMonitor.Settings;
using BatteryMonitor.Tests.Mocks;

public class SchedulerTimingTests : IDisposable
{
    private readonly string _testBaseDir;
    private readonly string _testAppDataDir;
    private readonly ConfigManager _configManager;
    private readonly FakeTimeProvider _timeProvider;
    private readonly MockEnumerator _enumerator;
    private readonly MockDriver _driver;

    public SchedulerTimingTests()
    {
        _testBaseDir = Path.Combine(Path.GetTempPath(), "BM_SchedTest_Base_" + Guid.NewGuid().ToString("N"));
        _testAppDataDir = Path.Combine(Path.GetTempPath(), "BM_SchedTest_AppData_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testBaseDir);
        Directory.CreateDirectory(_testAppDataDir);

        _configManager = new ConfigManager(_testBaseDir, _testAppDataDir);
        _configManager.Load();

        _timeProvider = new FakeTimeProvider();
        _enumerator = new MockEnumerator();
        _driver = new MockDriver();
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
    public async Task Scheduler_PeriodicTicks_PollsAtConfiguredInterval()
    {
        var devInfo = new DeviceInfo("DEV_01", @"\\?\hid#01", 0x046D, 0xC547, "Mouse", DeviceType.Mouse, TransportType.UsbHid);
        _enumerator.DeviceList.Add(devInfo);

        int pollCount = 0;
        await using var scheduler = new PollingScheduler(_enumerator, new[] { _driver }, _configManager, _timeProvider);
        scheduler.PollCompleted += (_, _) => pollCount++;

        using var cts = new CancellationTokenSource();
        await scheduler.StartAsync(cts.Token);

        // Wait a few ms for initial poll cycle to start and finish
        await Task.Delay(50);
        Assert.Equal(1, pollCount);

        // Advance fake clock by 60s
        _timeProvider.Advance(TimeSpan.FromSeconds(60));
        await Task.Delay(50);
        Assert.Equal(2, pollCount);

        // Advance fake clock by another 60s
        _timeProvider.Advance(TimeSpan.FromSeconds(60));
        await Task.Delay(50);
        Assert.Equal(3, pollCount);

        cts.Cancel();
    }

    [Fact]
    public async Task DeviceArrival_TriggersImmediateRefreshWithoutWaiting60Seconds()
    {
        await using var scheduler = new PollingScheduler(_enumerator, new[] { _driver }, _configManager, _timeProvider);

        int pollCount = 0;
        scheduler.PollCompleted += (_, _) => pollCount++;

        using var cts = new CancellationTokenSource();
        await scheduler.StartAsync(cts.Token);
        await Task.Delay(50);
        Assert.Equal(1, pollCount);

        // An arrival occurs while scheduler is waiting in 60s sleep
        var newDevice = new DeviceInfo("DEV_ARRIVED", @"\\?\hid#arr", 0x046D, 0x1234, "New Mouse", DeviceType.Mouse, TransportType.UsbHid);
        _enumerator.DeviceList.Add(newDevice);
        scheduler.NotifyDeviceArrival(newDevice);

        // Must wake up immediately and poll without advancing clock
        await Task.Delay(100);
        Assert.True(pollCount >= 2);
        Assert.True(scheduler.Devices.ContainsKey("DEV_ARRIVED"));

        cts.Cancel();
    }

    [Fact]
    public async Task DeviceRemoval_TransitionsDeviceToOffline()
    {
        var devInfo = new DeviceInfo("DEV_REM", @"\\?\hid#rem", 0x046D, 0x1234, "To Remove", DeviceType.Mouse, TransportType.UsbHid);
        _enumerator.DeviceList.Add(devInfo);

        await using var scheduler = new PollingScheduler(_enumerator, new[] { _driver }, _configManager, _timeProvider);
        using var cts = new CancellationTokenSource();
        await scheduler.StartAsync(cts.Token);
        await Task.Delay(50);

        Assert.Equal(BatteryState.Online, scheduler.Devices["DEV_REM"].State);

        // Remove from hardware enumerator and notify scheduler
        _enumerator.DeviceList.Remove(devInfo);
        scheduler.NotifyDeviceRemoval("DEV_REM");
        await Task.Delay(100);

        Assert.Equal(BatteryState.Offline, scheduler.Devices["DEV_REM"].State);
        cts.Cancel();
    }

    [Fact]
    public async Task WakeFromSleep_TransitionsToUnknown_Delays2s_Refreshes()
    {
        var devInfo = new DeviceInfo("DEV_WAKE", @"\\?\hid#wake", 0x046D, 0x1234, "Wake Mouse", DeviceType.Mouse, TransportType.UsbHid);
        _enumerator.DeviceList.Add(devInfo);

        await using var scheduler = new PollingScheduler(_enumerator, new[] { _driver }, _configManager, _timeProvider);
        using var cts = new CancellationTokenSource();
        await scheduler.StartAsync(cts.Token);
        await Task.Delay(50);

        Assert.Equal(BatteryState.Online, scheduler.Devices["DEV_WAKE"].State);

        // Simulate sleep suspend
        scheduler.NotifySystemSuspend();
        Assert.Equal(BatteryState.Unknown, scheduler.Devices["DEV_WAKE"].State);

        // Simulate wake resume
        scheduler.NotifySystemResume();
        Assert.Equal(BatteryState.Unknown, scheduler.Devices["DEV_WAKE"].State);

        // Advance 2-second stabilization delay
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        await Task.Delay(100);

        // Device should be refreshed back to Online
        Assert.Equal(BatteryState.Online, scheduler.Devices["DEV_WAKE"].State);

        cts.Cancel();
    }

    [Fact]
    public async Task PerDeviceIsolation_ExceptionInOneDriver_DoesNotStopOtherDevices()
    {
        var devBad = new DeviceInfo("DEV_BAD", @"\\?\hid#bad", 0x046D, 0x0001, "Bad Device", DeviceType.Mouse, TransportType.UsbHid);
        var devGood = new DeviceInfo("DEV_GOOD", @"\\?\hid#good", 0x046D, 0x0002, "Good Device", DeviceType.Keyboard, TransportType.UsbHid);

        _enumerator.DeviceList.Add(devBad);
        _enumerator.DeviceList.Add(devGood);

        var badDriver = new MockDriver
        {
            DriverId = "BadDriver",
            Priority = 5,
            MatchPredicate = d => d.DeviceId == "DEV_BAD",
            ReadHandler = (_, _) => throw new InvalidOperationException("Simulated hardware bus fault!")
        };

        var goodDriver = new MockDriver
        {
            DriverId = "GoodDriver",
            Priority = 10,
            MatchPredicate = d => d.DeviceId == "DEV_GOOD",
            ReadHandler = (_, _) => Task.FromResult<BatteryReading?>(new BatteryReading(92, ChargingStatus.Discharging))
        };

        await using var scheduler = new PollingScheduler(
            _enumerator,
            new IBatteryDriver[] { badDriver, goodDriver },
            _configManager,
            _timeProvider
        );

        using var cts = new CancellationTokenSource();
        await scheduler.StartAsync(cts.Token);
        await Task.Delay(80);

        // Bad device failed safely and recorded failure
        Assert.True(scheduler.Devices.ContainsKey("DEV_BAD"));
        Assert.Equal(1, scheduler.Devices["DEV_BAD"].ConsecutiveFailures);

        // Good device read successfully despite bad device exception
        Assert.True(scheduler.Devices.ContainsKey("DEV_GOOD"));
        Assert.Equal(BatteryState.Online, scheduler.Devices["DEV_GOOD"].State);
        Assert.Equal(92, scheduler.Devices["DEV_GOOD"].Percentage);

        cts.Cancel();
    }

    [Fact]
    public async Task PerDeviceIsolation_TimeoutInOneDriver_DoesNotHangOtherDevices()
    {
        var devHanging = new DeviceInfo("DEV_HANG", @"\\?\hid#hang", 0x046D, 0x0001, "Hanging Device", DeviceType.Headset, TransportType.UsbHid);
        var devFast = new DeviceInfo("DEV_FAST", @"\\?\hid#fast", 0x046D, 0x0002, "Fast Device", DeviceType.Controller, TransportType.UsbHid);

        _enumerator.DeviceList.Add(devHanging);
        _enumerator.DeviceList.Add(devFast);

        var hangingDriver = new MockDriver
        {
            DriverId = "HangingDriver",
            Priority = 5,
            MatchPredicate = d => d.DeviceId == "DEV_HANG",
            ReadHandler = async (_, ct) =>
            {
                // Hangs until canceled by timeout token
                await Task.Delay(Timeout.Infinite, ct);
                return null;
            }
        };

        var fastDriver = new MockDriver
        {
            DriverId = "FastDriver",
            Priority = 10,
            MatchPredicate = d => d.DeviceId == "DEV_FAST",
            ReadHandler = (_, _) => Task.FromResult<BatteryReading?>(new BatteryReading(77, ChargingStatus.Charging))
        };

        // Configure a short 100ms device timeout for test speed
        await using var scheduler = new PollingScheduler(
            _enumerator,
            new IBatteryDriver[] { hangingDriver, fastDriver },
            _configManager,
            _timeProvider,
            deviceTimeout: TimeSpan.FromMilliseconds(100)
        );

        using var cts = new CancellationTokenSource();
        await scheduler.StartAsync(cts.Token);

        // Wait for 100ms timeout to elapse on hanging device + fast device to execute
        await Task.Delay(250);

        // Hanging device should have timed out and recorded failure
        Assert.True(scheduler.Devices.ContainsKey("DEV_HANG"));
        Assert.Equal(1, scheduler.Devices["DEV_HANG"].ConsecutiveFailures);

        // Fast device read cleanly
        Assert.True(scheduler.Devices.ContainsKey("DEV_FAST"));
        Assert.Equal(BatteryState.Charging, scheduler.Devices["DEV_FAST"].State);
        Assert.Equal(77, scheduler.Devices["DEV_FAST"].Percentage);

        cts.Cancel();
    }

    [Fact]
    public async Task IgnoreList_SkipsIgnoredDevices()
    {
        var devIgnored = new DeviceInfo("DEV_IGNORE", @"\\?\hid#ign", 0x046D, 0x0001, "Ignored Mouse", DeviceType.Mouse, TransportType.UsbHid);
        var devNormal = new DeviceInfo("DEV_NORMAL", @"\\?\hid#norm", 0x046D, 0x0002, "Normal Mouse", DeviceType.Mouse, TransportType.UsbHid);

        _enumerator.DeviceList.Add(devIgnored);
        _enumerator.DeviceList.Add(devNormal);

        _configManager.Update(cfg => cfg.IgnoreList.Add("DEV_IGNORE"));

        await using var scheduler = new PollingScheduler(_enumerator, new[] { _driver }, _configManager, _timeProvider);
        using var cts = new CancellationTokenSource();
        await scheduler.StartAsync(cts.Token);
        await Task.Delay(50);

        // DEV_IGNORE is not tracked
        Assert.False(scheduler.Devices.ContainsKey("DEV_IGNORE"));

        // DEV_NORMAL is tracked
        Assert.True(scheduler.Devices.ContainsKey("DEV_NORMAL"));

        cts.Cancel();
    }
}
