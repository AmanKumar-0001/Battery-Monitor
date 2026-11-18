namespace BatteryMonitor.Ui.Taskbar;

using BatteryMonitor.Interop;

public static class DpiHelper
{
    public const double StandardDpi = 96.0;

    public static double GetDpiScaleForWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return 1.0;
        }

        try
        {
            uint dpi = Win32WindowPInvoke.GetDpiForWindow(hWnd);
            if (dpi == 0)
            {
                return 1.0;
            }
            return dpi / StandardDpi;
        }
        catch
        {
            return 1.0;
        }
    }

    public static double Scale(double unscaledValue, double dpiScale)
    {
        return unscaledValue * dpiScale;
    }
}
