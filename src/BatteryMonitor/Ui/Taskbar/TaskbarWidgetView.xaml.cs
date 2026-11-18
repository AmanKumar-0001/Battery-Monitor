namespace BatteryMonitor.Ui.Taskbar;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using BatteryMonitor.Core.Models;
using BatteryMonitor.Interop;
using BatteryMonitor.Settings;

public partial class TaskbarWidgetView : Window
{
    private readonly ThemeWatcher _themeWatcher;

    public TaskbarWidgetView(ThemeWatcher themeWatcher)
    {
        InitializeComponent();
        _themeWatcher = themeWatcher ?? throw new ArgumentNullException(nameof(themeWatcher));

        SourceInitialized += OnSourceInitialized;
        _themeWatcher.ThemeChanged += OnThemeChanged;
        ApplyTheme();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            // Set extended window styles: Layered, ToolWindow, NoActivate
            IntPtr exStyle = Win32WindowPInvoke.GetWindowLongPtr(handle, Win32WindowPInvoke.GWL_EXSTYLE);
            long newExStyle = exStyle.ToInt64()
                | Win32WindowPInvoke.WS_EX_LAYERED
                | Win32WindowPInvoke.WS_EX_TOOLWINDOW
                | Win32WindowPInvoke.WS_EX_NOACTIVATE;
            Win32WindowPInvoke.SetWindowLongPtr(handle, Win32WindowPInvoke.GWL_EXSTYLE, new IntPtr(newExStyle));
        }
    }

    private void OnThemeChanged(object? sender, bool isDark)
    {
        Dispatcher.InvokeAsync(ApplyTheme);
    }

    public void ApplyTheme()
    {
        RootBorder.Background = _themeWatcher.BackgroundBrush;
        RootBorder.BorderBrush = _themeWatcher.BorderBrush;
    }

    public void UpdateDevices(IEnumerable<BatteryDevice> devices, AppConfig config)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => UpdateDevices(devices, config));
            return;
        }

        DeviceContainer.Children.Clear();

        var visibleDevices = devices
            .Where(d => !d.IsHidden && d.State != BatteryState.Offline)
            .ToList();

        if (visibleDevices.Count == 0)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;

        for (int i = 0; i < visibleDevices.Count; i++)
        {
            var dev = visibleDevices[i];
            var chip = CreateDeviceChip(dev, config);
            DeviceContainer.Children.Add(chip);

            if (i < visibleDevices.Count - 1)
            {
                // Separator
                var sep = new Border
                {
                    Width = 1,
                    Height = 14,
                    Margin = new Thickness(6, 0, 6, 0),
                    Background = _themeWatcher.BorderBrush
                };
                DeviceContainer.Children.Add(sep);
            }
        }
    }

    private UIElement CreateDeviceChip(BatteryDevice dev, AppConfig config)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Device Icon Glyph
        string iconGlyph = dev.Info.Type switch
        {
            DeviceType.Mouse => "🖱️",
            DeviceType.Keyboard => "⌨️",
            DeviceType.Headset => "🎧",
            DeviceType.Controller => "🎮",
            _ => "🔋"
        };

        var iconText = new TextBlock
        {
            Text = iconGlyph,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        panel.Children.Add(iconText);

        // Charging or Level
        int lowThreshold = config.LowBatteryThreshold;
        int criticalThreshold = config.CriticalBatteryThreshold;
        if (config.Devices.TryGetValue(dev.Info.DeviceId, out var devCfg))
        {
            if (devCfg.CustomLowThreshold.HasValue) lowThreshold = devCfg.CustomLowThreshold.Value;
            if (devCfg.CustomCriticalThreshold.HasValue) criticalThreshold = devCfg.CustomCriticalThreshold.Value;
        }

        string pctText = dev.Percentage.HasValue ? $"{dev.Percentage.Value}%" : "--";
        if (dev.State == BatteryState.Charging)
        {
            pctText = $"⚡ {pctText}";
        }
        else if (dev.State == BatteryState.Asleep)
        {
            pctText = $"💤 {pctText}";
        }

        Brush textBrush = _themeWatcher.ForegroundBrush;
        if (dev.Percentage.HasValue && dev.State != BatteryState.Charging)
        {
            if (dev.Percentage.Value <= criticalThreshold)
            {
                textBrush = Brushes.Crimson;
            }
            else if (dev.Percentage.Value <= lowThreshold)
            {
                textBrush = Brushes.DarkOrange;
            }
        }

        var levelText = new TextBlock
        {
            Text = pctText,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = textBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(levelText);

        panel.ToolTip = $"{dev.DisplayName}: {pctText} ({dev.State})";
        return panel;
    }
}
