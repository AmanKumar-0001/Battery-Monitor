namespace BatteryMonitor.Tests.UiTests;

using BatteryMonitor.Ui.Taskbar;

public class ThemeWatcherTests
{
    [Fact]
    public void ThemeWatcher_InitializesBrushes()
    {
        var watcher = new ThemeWatcher();

        Assert.NotNull(watcher.BackgroundBrush);
        Assert.NotNull(watcher.ForegroundBrush);
        Assert.NotNull(watcher.SubtleBrush);
        Assert.NotNull(watcher.BorderBrush);
    }

    [Fact]
    public void ThemeWatcher_RefreshTheme_DoesNotThrow()
    {
        var watcher = new ThemeWatcher();
        var ex = Record.Exception(() => watcher.RefreshTheme());
        Assert.Null(ex);
    }
}

public class DpiHelperTests
{
    [Fact]
    public void DpiHelper_GetDpiScale_ZeroHandle_ReturnsOne()
    {
        double scale = DpiHelper.GetDpiScaleForWindow(IntPtr.Zero);
        Assert.Equal(1.0, scale);
    }

    [Theory]
    [InlineData(100.0, 1.0, 100.0)]
    [InlineData(100.0, 1.25, 125.0)]
    [InlineData(100.0, 1.5, 150.0)]
    [InlineData(100.0, 2.0, 200.0)]
    public void DpiHelper_Scale_MultipliesCorrectly(double unscaled, double scaleFactor, double expected)
    {
        double result = DpiHelper.Scale(unscaled, scaleFactor);
        Assert.Equal(expected, result);
    }
}
