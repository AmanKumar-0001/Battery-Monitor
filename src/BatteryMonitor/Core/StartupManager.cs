namespace BatteryMonitor.Core;

using Microsoft.Win32;

public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "BatteryMonitor";

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(RunValueName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetStartup(bool enable, string? exePath = null)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return;

            if (enable)
            {
                string targetPath = exePath ?? Environment.ProcessPath ?? "";
                if (!string.IsNullOrWhiteSpace(targetPath))
                {
                    // Strictly store the quoted path
                    key.SetValue(RunValueName, $"\"{targetPath}\"");
                }
            }
            else
            {
                if (key.GetValue(RunValueName) != null)
                {
                    key.DeleteValue(RunValueName, false);
                }
            }
        }
        catch
        {
            // Non-fatal: do not crash if registry write fails
        }
    }

    /// <summary>
    /// Self-healing startup path: If the registry Run value exists but points to an old/moved path,
    /// automatically updates it to the current running executable's path.
    /// </summary>
    public static bool SyncStartupPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return false;

            object? rawVal = key.GetValue(RunValueName);
            if (rawVal is string storedVal && !string.IsNullOrWhiteSpace(storedVal))
            {
                string cleanStored = storedVal.Trim().Trim('"');
                string currentPath = Environment.ProcessPath ?? "";

                if (!string.IsNullOrWhiteSpace(currentPath) &&
                    !string.Equals(cleanStored, currentPath, StringComparison.OrdinalIgnoreCase))
                {
                    // Update to current location
                    key.SetValue(RunValueName, $"\"{currentPath}\"");
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }
}
