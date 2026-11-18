namespace BatteryMonitor.Tests.CoreTests;

using BatteryMonitor.Settings;

public class ConfigTests : IDisposable
{
    private readonly string _testBaseDir;
    private readonly string _testAppDataDir;

    public ConfigTests()
    {
        _testBaseDir = Path.Combine(Path.GetTempPath(), "BM_ConfigTest_Base_" + Guid.NewGuid().ToString("N"));
        _testAppDataDir = Path.Combine(Path.GetTempPath(), "BM_ConfigTest_AppData_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testBaseDir);
        Directory.CreateDirectory(_testAppDataDir);
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
    public void StandardMode_ResolvesAppDataPath()
    {
        var manager = new ConfigManager(_testBaseDir, _testAppDataDir);
        var config = manager.Load();

        Assert.False(manager.IsPortableMode);
        Assert.Contains(_testAppDataDir, manager.ConfigFilePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(manager.ConfigFilePath));
        Assert.Equal(20, config.LowBatteryThreshold);
        Assert.Equal(10, config.CriticalBatteryThreshold);
    }

    [Fact]
    public void PortableMode_OverridesWhenFileExistsInBaseDirectory()
    {
        string portableFile = Path.Combine(_testBaseDir, "BatteryMonitor.json");
        File.WriteAllText(portableFile, "{ \"pollingIntervalSeconds\": 45 }");

        var manager = new ConfigManager(_testBaseDir, _testAppDataDir);
        var config = manager.Load();

        Assert.True(manager.IsPortableMode);
        Assert.Equal(portableFile, manager.ConfigFilePath);
        Assert.Equal(45, config.PollingIntervalSeconds);
    }

    [Fact]
    public void Config_RoundTripSaveAndReload()
    {
        var manager = new ConfigManager(_testBaseDir, _testAppDataDir);
        manager.Load();

        manager.Update(cfg =>
        {
            cfg.PollingIntervalSeconds = 30;
            cfg.LowBatteryThreshold = 25;
            cfg.CriticalBatteryThreshold = 15;
            cfg.Devices["DEV_TEST"] = new DeviceConfig
            {
                Nickname = "Gaming Mouse",
                IsHidden = true,
                CustomLowThreshold = 30
            };
            cfg.IgnoreList.Add("IGNORED_DEV");
        });

        // Create a new instance pointing to the same folder and reload
        var reloadManager = new ConfigManager(_testBaseDir, _testAppDataDir);
        var reloaded = reloadManager.Load();

        Assert.Equal(30, reloaded.PollingIntervalSeconds);
        Assert.Equal(25, reloaded.LowBatteryThreshold);
        Assert.Equal(15, reloaded.CriticalBatteryThreshold);
        Assert.True(reloaded.Devices.ContainsKey("DEV_TEST"));
        Assert.Equal("Gaming Mouse", reloaded.Devices["DEV_TEST"].Nickname);
        Assert.True(reloaded.Devices["DEV_TEST"].IsHidden);
        Assert.Equal(30, reloaded.Devices["DEV_TEST"].CustomLowThreshold);
        Assert.Contains("IGNORED_DEV", reloaded.IgnoreList);
    }

    [Fact]
    public void CorruptConfig_RecoversWithDefaultAndCreatesBackup()
    {
        var manager = new ConfigManager(_testBaseDir, _testAppDataDir);
        manager.Load();

        // Deliberately corrupt the file with invalid JSON syntax
        File.WriteAllText(manager.ConfigFilePath, "{ \"brokenJson\": [ incomplete... ");

        var reloadManager = new ConfigManager(_testBaseDir, _testAppDataDir);
        var recovered = reloadManager.Load();

        // Must recover safely to default config
        Assert.NotNull(recovered);
        Assert.Equal(20, recovered.LowBatteryThreshold);
        Assert.Equal(10, recovered.CriticalBatteryThreshold);

        // Verify backup file was created
        string dir = Path.GetDirectoryName(manager.ConfigFilePath)!;
        var backupFiles = Directory.GetFiles(dir, "config.json.corrupt.*");
        Assert.NotEmpty(backupFiles);

        // Verify clean config was rewritten
        string cleanContent = File.ReadAllText(manager.ConfigFilePath);
        Assert.Contains("\"lowBatteryThreshold\": 20", cleanContent, StringComparison.OrdinalIgnoreCase);
    }
}
