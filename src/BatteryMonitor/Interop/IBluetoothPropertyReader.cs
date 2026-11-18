namespace BatteryMonitor.Interop;

public interface IBluetoothPropertyReader
{
    /// <summary>
    /// Reads the cached Windows Bluetooth DEVPKEY_Device_BatteryStrength property (0-100%)
    /// for the given device ID or device path. Returns null if unavailable.
    /// </summary>
    byte? ReadBatteryStrength(string devicePathOrId);
}
