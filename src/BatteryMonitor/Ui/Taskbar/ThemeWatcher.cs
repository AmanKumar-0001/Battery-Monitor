namespace BatteryMonitor.Ui.Taskbar;

using System.Windows.Media;
using Microsoft.Win32;

public sealed class ThemeWatcher
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string SystemUsesLightThemeValue = "SystemUsesLightTheme";

    public bool IsDarkTheme { get; private set; } = true;

    public Brush BackgroundBrush => IsDarkTheme
        ? new SolidColorBrush(Color.FromArgb(210, 32, 32, 32))
        : new SolidColorBrush(Color.FromArgb(210, 243, 243, 243));

    public Brush ForegroundBrush => IsDarkTheme
        ? new SolidColorBrush(Color.FromRgb(255, 255, 255))
        : new SolidColorBrush(Color.FromRgb(26, 26, 26));

    public Brush SubtleBrush => IsDarkTheme
        ? new SolidColorBrush(Color.FromRgb(160, 160, 160))
        : new SolidColorBrush(Color.FromRgb(100, 100, 100));

    public Brush BorderBrush => IsDarkTheme
        ? new SolidColorBrush(Color.FromArgb(50, 255, 255, 255))
        : new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));

    public event EventHandler<bool>? ThemeChanged;

    public ThemeWatcher()
    {
        RefreshTheme();
    }

    public void RefreshTheme()
    {
        bool wasDark = IsDarkTheme;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            if (key?.GetValue(SystemUsesLightThemeValue) is int lightThemeInt)
            {
                IsDarkTheme = lightThemeInt == 0;
            }
            else
            {
                IsDarkTheme = true; // Default fallback to Dark
            }
        }
        catch
        {
            IsDarkTheme = true;
        }

        if (wasDark != IsDarkTheme)
        {
            ThemeChanged?.Invoke(this, IsDarkTheme);
        }
    }
}
