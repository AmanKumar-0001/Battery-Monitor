namespace BatteryMonitor.Ui.Tray;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using BatteryMonitor.Core.Models;
using BatteryMonitor.Core.Scheduler;
using BatteryMonitor.Interop;
using BatteryMonitor.Settings;

public sealed class TrayIconManager : IDisposable
{
    private const uint TrayIconId = 1001;

    private readonly IPollingScheduler _scheduler;
    private readonly ConfigManager _configManager;
    private readonly Action _openSettingsAction;
    private readonly Action _exitAction;
    private readonly uint _taskbarCreatedMsg;

    private HwndSource? _hwndSource;
    private IntPtr _hwnd;
    private bool _iconCreated;
    private bool _isDisposed;

    public TrayIconManager(
        IPollingScheduler scheduler,
        ConfigManager configManager,
        Action openSettingsAction,
        Action exitAction)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _openSettingsAction = openSettingsAction ?? throw new ArgumentNullException(nameof(openSettingsAction));
        _exitAction = exitAction ?? throw new ArgumentNullException(nameof(exitAction));
        _taskbarCreatedMsg = Win32WindowPInvoke.RegisterWindowMessage("TaskbarCreated");

        CreateMessageWindow();
        CreateTrayIcon();

        _scheduler.DeviceUpdated += OnDeviceUpdated;
    }

    private void CreateMessageWindow()
    {
        var parameters = new HwndSourceParameters("BatteryMonitor_TrayMessageWindow")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            Width = 0,
            Height = 0
        };

        _hwndSource = new HwndSource(parameters);
        _hwnd = _hwndSource.Handle;
        _hwndSource.AddHook(WndProc);
    }

    private void CreateTrayIcon()
    {
        if (_hwnd == IntPtr.Zero) return;

        var nid = new Win32WindowPInvoke.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<Win32WindowPInvoke.NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = TrayIconId,
            uFlags = Win32WindowPInvoke.NIF_MESSAGE | Win32WindowPInvoke.NIF_ICON | Win32WindowPInvoke.NIF_TIP,
            uCallbackMessage = Win32WindowPInvoke.WM_TRAYICON,
            hIcon = Win32WindowPInvoke.LoadIcon(IntPtr.Zero, (IntPtr)Win32WindowPInvoke.IDI_APPLICATION),
            szTip = "Battery Monitor"
        };

        _iconCreated = Win32WindowPInvoke.Shell_NotifyIcon(Win32WindowPInvoke.NIM_ADD, ref nid);
    }

    public void UpdateTooltip(string tooltipText)
    {
        if (!_iconCreated || _hwnd == IntPtr.Zero) return;

        var nid = new Win32WindowPInvoke.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<Win32WindowPInvoke.NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = TrayIconId,
            uFlags = Win32WindowPInvoke.NIF_TIP,
            szTip = tooltipText.Length > 127 ? tooltipText[..127] : tooltipText
        };

        Win32WindowPInvoke.Shell_NotifyIcon(Win32WindowPInvoke.NIM_MODIFY, ref nid);
    }

    private void OnDeviceUpdated(object? sender, BatteryDevice device)
    {
        var primary = _scheduler.Devices.Values.FirstOrDefault(d => !d.IsHidden && d.State != BatteryState.Offline);
        if (primary != null && primary.Percentage.HasValue)
        {
            string stateLabel = primary.State == BatteryState.Charging ? "⚡" : "";
            UpdateTooltip($"Battery Monitor\n{primary.DisplayName}: {primary.Percentage.Value}% {stateLabel}");
        }
        else
        {
            UpdateTooltip("Battery Monitor - No active devices");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == _taskbarCreatedMsg)
        {
            // Re-create tray icon when Explorer restarts
            CreateTrayIcon();
            handled = true;
        }
        else if (msg == (int)Win32WindowPInvoke.WM_TRAYICON)
        {
            int eventId = lParam.ToInt32() & 0xFFFF;
            if (eventId == 0x0205) // WM_RBUTTONUP
            {
                ShowContextMenu();
                handled = true;
            }
            else if (eventId == 0x0202) // WM_LBUTTONUP
            {
                _openSettingsAction();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    public ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var header = new MenuItem
        {
            Header = "Battery Monitor",
            FontWeight = FontWeights.Bold,
            IsEnabled = false
        };
        menu.Items.Add(header);
        menu.Items.Add(new Separator());

        var activeDevices = _scheduler.Devices.Values
            .Where(d => d.State != BatteryState.Offline)
            .ToList();

        if (activeDevices.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "(No devices detected)", IsEnabled = false });
        }
        else
        {
            foreach (var dev in activeDevices)
            {
                string icon = dev.Info.Type switch
                {
                    DeviceType.Mouse => "🖱️",
                    DeviceType.Keyboard => "⌨️",
                    DeviceType.Headset => "🎧",
                    DeviceType.Controller => "🎮",
                    _ => "🔋"
                };

                string pct = dev.Percentage.HasValue ? $"{dev.Percentage.Value}%" : "--";
                string charge = dev.State == BatteryState.Charging ? " [⚡ Charging]" : "";
                string devHeader = $"{icon} {dev.DisplayName}: {pct}{charge}";

                var devItem = new MenuItem { Header = devHeader };

                var toggleItem = new MenuItem
                {
                    Header = "Show in Taskbar Widget",
                    IsCheckable = true,
                    IsChecked = !dev.IsHidden
                };

                toggleItem.Click += (s, e) =>
                {
                    dev.IsHidden = !toggleItem.IsChecked;
                    _configManager.Update(cfg =>
                    {
                        if (!cfg.Devices.TryGetValue(dev.Info.DeviceId, out var devCfg))
                        {
                            devCfg = new DeviceConfig();
                            cfg.Devices[dev.Info.DeviceId] = devCfg;
                        }
                        devCfg.IsHidden = dev.IsHidden;
                    });
                };

                devItem.Items.Add(toggleItem);
                menu.Items.Add(devItem);
            }
        }

        menu.Items.Add(new Separator());

        // Refresh now
        var refreshItem = new MenuItem { Header = "🔄 Refresh Now" };
        refreshItem.Click += async (s, e) =>
        {
            await _scheduler.TriggerImmediateRefreshAsync("TrayManualRefresh");
        };
        menu.Items.Add(refreshItem);

        // Start with Windows
        var startWithWindowsItem = new MenuItem
        {
            Header = "Start with Windows",
            IsCheckable = true,
            IsChecked = _configManager.Current.StartWithWindows
        };
        startWithWindowsItem.Click += (s, e) =>
        {
            _configManager.Update(cfg => cfg.StartWithWindows = startWithWindowsItem.IsChecked);
        };
        menu.Items.Add(startWithWindowsItem);

        // Settings
        var settingsItem = new MenuItem { Header = "⚙️ Settings..." };
        settingsItem.Click += (s, e) => _openSettingsAction();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new Separator());

        // Exit
        var exitItem = new MenuItem { Header = "❌ Exit" };
        exitItem.Click += (s, e) => _exitAction();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void ShowContextMenu()
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var menu = BuildContextMenu();
            menu.IsOpen = true;
        });
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_iconCreated && _hwnd != IntPtr.Zero)
        {
            var nid = new Win32WindowPInvoke.NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<Win32WindowPInvoke.NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = TrayIconId
            };
            Win32WindowPInvoke.Shell_NotifyIcon(Win32WindowPInvoke.NIM_DELETE, ref nid);
            _iconCreated = false;
        }

        _hwndSource?.RemoveHook(WndProc);
        _hwndSource?.Dispose();
    }
}
