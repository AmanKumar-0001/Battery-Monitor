namespace BatteryMonitor.Settings;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BatteryMonitor.Core.Models;
using BatteryMonitor.Core.Scheduler;

public sealed class DeviceSettingsItem : INotifyPropertyChanged
{
    private string _nickname;
    private bool _isHidden;
    private int? _customLowThreshold;

    public string DeviceId { get; }
    public string OriginalName { get; }
    public DeviceType Type { get; }

    public string Nickname
    {
        get => _nickname;
        set
        {
            if (_nickname != value)
            {
                _nickname = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsHidden
    {
        get => _isHidden;
        set
        {
            if (_isHidden != value)
            {
                _isHidden = value;
                OnPropertyChanged();
            }
        }
    }

    public int? CustomLowThreshold
    {
        get => _customLowThreshold;
        set
        {
            if (_customLowThreshold != value)
            {
                _customLowThreshold = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DeviceSettingsItem(string deviceId, string originalName, DeviceType type, string? nickname, bool isHidden, int? customLowThreshold)
    {
        DeviceId = deviceId;
        OriginalName = originalName;
        Type = type;
        _nickname = string.IsNullOrWhiteSpace(nickname) ? originalName : nickname;
        _isHidden = isHidden;
        _customLowThreshold = customLowThreshold;
    }

    private void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ConfigManager _configManager;
    private readonly IPollingScheduler _scheduler;

    public ObservableCollection<DeviceSettingsItem> DeviceItems { get; } = new();

    public int LowBatteryThreshold
    {
        get => _configManager.Current.LowBatteryThreshold;
        set
        {
            if (_configManager.Current.LowBatteryThreshold != value)
            {
                _configManager.Update(cfg => cfg.LowBatteryThreshold = Math.Clamp(value, 5, 50));
                OnPropertyChanged();
            }
        }
    }

    public int CriticalBatteryThreshold
    {
        get => _configManager.Current.CriticalBatteryThreshold;
        set
        {
            if (_configManager.Current.CriticalBatteryThreshold != value)
            {
                _configManager.Update(cfg => cfg.CriticalBatteryThreshold = Math.Clamp(value, 1, 20));
                OnPropertyChanged();
            }
        }
    }

    public int PollingIntervalSeconds
    {
        get => _configManager.Current.PollingIntervalSeconds;
        set
        {
            if (_configManager.Current.PollingIntervalSeconds != value)
            {
                _configManager.Update(cfg => cfg.PollingIntervalSeconds = Math.Clamp(value, 10, 300));
                OnPropertyChanged();
            }
        }
    }

    public bool DemoMode
    {
        get => _configManager.Current.DemoMode;
        set
        {
            if (_configManager.Current.DemoMode != value)
            {
                _configManager.Update(cfg => cfg.DemoMode = value);
                OnPropertyChanged();
            }
        }
    }

    public bool StartWithWindows
    {
        get => _configManager.Current.StartWithWindows;
        set
        {
            if (_configManager.Current.StartWithWindows != value)
            {
                _configManager.Update(cfg => cfg.StartWithWindows = value);
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel(ConfigManager configManager, IPollingScheduler scheduler)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));

        LoadDevices();
    }

    public void LoadDevices()
    {
        DeviceItems.Clear();
        var cfg = _configManager.Current;

        foreach (var device in _scheduler.Devices.Values)
        {
            string? nickname = null;
            bool isHidden = device.IsHidden;
            int? customLow = null;

            if (cfg.Devices.TryGetValue(device.Info.DeviceId, out var devCfg))
            {
                nickname = devCfg.Nickname;
                isHidden = devCfg.IsHidden;
                customLow = devCfg.CustomLowThreshold;
            }

            var item = new DeviceSettingsItem(
                device.Info.DeviceId,
                device.Info.Name,
                device.Info.Type,
                nickname,
                isHidden,
                customLow
            );

            item.PropertyChanged += (s, e) =>
            {
                SaveDeviceItem(item);
            };

            DeviceItems.Add(item);
        }
    }

    public void SaveDeviceItem(DeviceSettingsItem item)
    {
        _configManager.Update(cfg =>
        {
            if (!cfg.Devices.TryGetValue(item.DeviceId, out var devCfg))
            {
                devCfg = new DeviceConfig();
                cfg.Devices[item.DeviceId] = devCfg;
            }

            devCfg.Nickname = item.Nickname;
            devCfg.IsHidden = item.IsHidden;
            devCfg.CustomLowThreshold = item.CustomLowThreshold;
        });

        // Update in-memory tracked device
        if (_scheduler.Devices.TryGetValue(item.DeviceId, out var device))
        {
            device.DisplayName = item.Nickname;
            device.IsHidden = item.IsHidden;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
