namespace BatteryMonitor.Ui.Taskbar;

using System.Windows.Interop;
using BatteryMonitor.Interop;

public sealed class TaskbarHost : IDisposable
{
    private readonly TaskbarWidgetView _widget;
    private readonly uint _taskbarCreatedMsg;
    private HwndSource? _hwndSource;
    private bool _isDisposed;

    public TaskbarHost(TaskbarWidgetView widget)
    {
        _widget = widget ?? throw new ArgumentNullException(nameof(widget));
        _taskbarCreatedMsg = Win32WindowPInvoke.RegisterWindowMessage("TaskbarCreated");

        _widget.Loaded += OnWidgetLoaded;
    }

    private void OnWidgetLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(_widget).Handle;
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);

        RepositionWidget();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == _taskbarCreatedMsg)
        {
            // Windows Explorer restarted
            RepositionWidget();
            handled = true;
        }
        else if ((uint)msg == Win32WindowPInvoke.WM_DPICHANGED)
        {
            RepositionWidget();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void RepositionWidget()
    {
        if (_isDisposed) return;

        IntPtr hTray = Win32WindowPInvoke.FindWindow("Shell_TrayWnd", null);
        if (hTray == IntPtr.Zero)
        {
            return;
        }

        bool gotRect = Win32WindowPInvoke.GetWindowRect(hTray, out var taskbarRect);
        if (!gotRect)
        {
            return;
        }

        IntPtr hNotify = Win32WindowPInvoke.FindWindowEx(hTray, IntPtr.Zero, "TrayNotifyWnd", null);
        Win32WindowPInvoke.RECT notifyRect = taskbarRect;
        if (hNotify != IntPtr.Zero)
        {
            Win32WindowPInvoke.GetWindowRect(hNotify, out notifyRect);
        }

        _widget.Dispatcher.InvokeAsync(() =>
        {
            double scale = DpiHelper.GetDpiScaleForWindow(new WindowInteropHelper(_widget).Handle);
            double widgetWidth = _widget.ActualWidth > 0 ? _widget.ActualWidth : 120;
            double widgetHeight = _widget.ActualHeight > 0 ? _widget.ActualHeight : 32;

            // Position to the left of the notification area (TrayNotifyWnd)
            double targetX = (notifyRect.Left / scale) - widgetWidth - 8;
            double targetY = (taskbarRect.Top / scale) + ((taskbarRect.Height / scale - widgetHeight) / 2);

            _widget.Left = Math.Max(0, targetX);
            _widget.Top = Math.Max(0, targetY);

            if (!_widget.IsVisible)
            {
                _widget.Show();
            }
        });
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource?.Dispose();
    }
}
