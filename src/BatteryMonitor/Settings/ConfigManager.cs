namespace BatteryMonitor.Settings;

using System.IO;
using System.Text.Json;

public sealed class ConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _syncLock = new();
    private readonly string? _customBasePath;
    private readonly string? _customAppDataPath;
    private AppConfig _currentConfig;

    public string ConfigFilePath { get; private set; }
    public bool IsPortableMode { get; private set; }

    public AppConfig Current
    {
        get
        {
            lock (_syncLock)
            {
                return _currentConfig;
            }
        }
    }

    public ConfigManager(string? customBasePath = null, string? customAppDataPath = null)
    {
        _customBasePath = customBasePath;
        _customAppDataPath = customAppDataPath;
        _currentConfig = new AppConfig();
        ConfigFilePath = ResolveConfigPath();
    }

    private string ResolveConfigPath()
    {
        string baseDir = _customBasePath ?? AppDomain.CurrentDomain.BaseDirectory;
        string portableFile = Path.Combine(baseDir, "BatteryMonitor.json");

        if (File.Exists(portableFile))
        {
            IsPortableMode = true;
            return portableFile;
        }

        IsPortableMode = false;
        string appDataRoot = _customAppDataPath ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string appDataDir = Path.Combine(appDataRoot, "BatteryMonitor");
        return Path.Combine(appDataDir, "config.json");
    }

    public AppConfig Load()
    {
        lock (_syncLock)
        {
            ConfigFilePath = ResolveConfigPath();

            if (!File.Exists(ConfigFilePath))
            {
                _currentConfig = new AppConfig();
                SaveInternal(_currentConfig);
                return _currentConfig;
            }

            try
            {
                string json = File.ReadAllText(ConfigFilePath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (loaded == null)
                {
                    throw new JsonException("Deserialization returned null.");
                }
                _currentConfig = loaded;
                return _currentConfig;
            }
            catch (Exception)
            {
                // Corrupt configuration recovery
                RecoverCorruptConfig();
                _currentConfig = new AppConfig();
                SaveInternal(_currentConfig);
                return _currentConfig;
            }
        }
    }

    private void RecoverCorruptConfig()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string corruptBackupPath = $"{ConfigFilePath}.corrupt.{timestamp}";
                File.Copy(ConfigFilePath, corruptBackupPath, overwrite: true);
            }
        }
        catch
        {
            // Best effort backup - do not crash if backup fails
        }
    }

    public void Save()
    {
        lock (_syncLock)
        {
            SaveInternal(_currentConfig);
        }
    }

    public void Update(Action<AppConfig> updateAction)
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        lock (_syncLock)
        {
            updateAction(_currentConfig);
            SaveInternal(_currentConfig);
        }
    }

    private void SaveInternal(AppConfig config)
    {
        string dir = Path.GetDirectoryName(ConfigFilePath)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tempPath = $"{ConfigFilePath}.tmp";
        string json = JsonSerializer.Serialize(config, JsonOptions);

        File.WriteAllText(tempPath, json);
        File.Move(tempPath, ConfigFilePath, overwrite: true);
    }
}
