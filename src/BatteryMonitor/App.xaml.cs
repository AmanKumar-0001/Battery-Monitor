namespace BatteryMonitor;

using System.IO;
using System.Threading;
using System.Windows;
using BatteryMonitor.Cli;
using BatteryMonitor.Core;
using BatteryMonitor.Core.Models;
using BatteryMonitor.Core.Scheduler;
using BatteryMonitor.Drivers;
using BatteryMonitor.Fallbacks;
using BatteryMonitor.History;
using BatteryMonitor.Interop;
using BatteryMonitor.Notifications;
using BatteryMonitor.Settings;
using BatteryMonitor.Ui.Taskbar;
using BatteryMonitor.Ui.Tray;

public partial class App : Application
{
    private ConfigManager? _configManager;
    private HistoryRepository? _historyRepository;
    private PollingScheduler? _scheduler;
    private TrayIconManager? _trayManager;
    private TaskbarWidgetView? _taskbarWidget;
    private TaskbarHost? _taskbarHost;
    private ThemeWatcher? _themeWatcher;
    private AlertCooldownTracker? _alertTracker;
    private INotificationService? _notificationService;
    private SettingsView? _settingsWindow;
    private CancellationTokenSource? _appCts;
    private EventWaitHandle? _quitEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _appCts = new CancellationTokenSource();

        // 1. Single-Instance Quit Signal Listener
        ListenForQuitSignal();

        // 2. Configuration
        _configManager = new ConfigManager();
        var config = _configManager.Load();

        // 3. Startup Registry Sync
        StartupManager.SyncStartupPath();

        // 4. Database Layer
        string dbPath = _configManager.IsPortableMode
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.db")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BatteryMonitor", "history.db");
        
        _historyRepository = new HistoryRepository(dbPath);
        _historyRepository.Initialize();

        // 5. UI Themes & Windows
        _themeWatcher = new ThemeWatcher();
        _taskbarWidget = new TaskbarWidgetView(_themeWatcher);
        _taskbarHost = new TaskbarHost(_taskbarWidget);

        // 6. Notifications
        _alertTracker = new AlertCooldownTracker();
        _notificationService = new ToastNotificationService();

        // 7. Drivers & Scheduler
        IDeviceEnumerator enumerator = new Win32DeviceEnumerator(_configManager);
        var drivers = new IBatteryDriver[]
        {
            new Hidpp20Driver(new Win32HidTransportFactory()),
            new GenericHidBatteryDriver(new Win32HidTransportFactory()),
            new BluetoothPropertyDriver(new Win32BluetoothPropertyReader()),
            new DemoBatteryDriver()
        };

        _scheduler = new PollingScheduler(enumerator, drivers, _configManager);
        _scheduler.DeviceUpdated += OnDeviceUpdated;
        _scheduler.PollCompleted += (s, e) => TrimWorkingSet();

        // 8. System Tray
        _trayManager = new TrayIconManager(
            _scheduler,
            _configManager,
            OpenSettingsWindow,
            ShutdownApplication
        );

        // 9. Start Background Polling
        _ = _scheduler.StartAsync(_appCts.Token);

        // 10. Memory Optimization: Schedule working set trim once UI and initial poll are idle
        _ = Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(2500);
            TrimWorkingSet();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    public static void TrimWorkingSet()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            Win32WindowPInvoke.EmptyWorkingSet(process.Handle);
        }
        catch
        {
        }
    }

    private void ListenForQuitSignal()
    {
        try
        {
            _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, CliHandler.QuitEventName);
            Task.Run(() =>
            {
                while (_quitEvent.WaitOne())
                {
                    Dispatcher.InvokeAsync(ShutdownApplication);
                    break;
                }
            });
        }
        catch
        {
        }
    }

    private void OnDeviceUpdated(object? sender, BatteryDevice device)
    {
        if (_configManager == null || _historyRepository == null || _alertTracker == null || _notificationService == null)
        {
            return;
        }

        var config = _configManager.Current;

        // Update taskbar widget
        if (_taskbarWidget != null && _scheduler != null)
        {
            _taskbarWidget.UpdateDevices(_scheduler.Devices.Values, config);
        }

        // Record history
        if (device.Percentage.HasValue)
        {
            _historyRepository.Enqueue(new BatteryHistoryEntry(
                0,
                device.Info.DeviceId,
                device.LastSeen,
                device.Percentage.Value,
                device.ChargingStatus,
                device.Voltage
            ));
        }

        // Evaluate notifications
        int low = config.LowBatteryThreshold;
        int crit = config.CriticalBatteryThreshold;
        if (config.Devices.TryGetValue(device.Info.DeviceId, out var devCfg))
        {
            if (devCfg.CustomLowThreshold.HasValue) low = devCfg.CustomLowThreshold.Value;
            if (devCfg.CustomCriticalThreshold.HasValue) crit = devCfg.CustomCriticalThreshold.Value;
        }

        var alert = _alertTracker.EvaluateAlert(
            device,
            low,
            crit,
            TimeSpan.FromMinutes(config.NotificationCooldownMinutes),
            DateTimeOffset.UtcNow
        );

        if (alert.HasValue && device.Percentage.HasValue)
        {
            _notificationService.ShowBatteryAlertAsync(device, alert.Value, device.Percentage.Value);
        }
    }

    public void OpenSettingsWindow()
    {
        if (_settingsWindow != null && _settingsWindow.IsVisible)
        {
            _settingsWindow.Activate();
            return;
        }

        if (_configManager != null && _scheduler != null)
        {
            var vm = new SettingsViewModel(_configManager, _scheduler);
            _settingsWindow = new SettingsView(vm);
            _settingsWindow.Closed += (s, e) => _settingsWindow = null;
            _settingsWindow.Show();
        }
    }

    public void ShutdownApplication()
    {
        _appCts?.Cancel();
        _trayManager?.Dispose();
        _taskbarHost?.Dispose();
        _taskbarWidget?.Close();

        _historyRepository?.Dispose();
        _quitEvent?.Dispose();

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _appCts?.Cancel();
        _historyRepository?.Dispose();
        base.OnExit(e);
    }
}
