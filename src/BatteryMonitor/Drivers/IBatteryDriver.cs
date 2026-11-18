namespace BatteryMonitor.Drivers;

using BatteryMonitor.Core.Models;

public interface IBatteryDriver
{
    /// <summary>
    /// Unique identifier for the driver (e.g., "Logitech.HID++2.0", "HID.Generic", "Windows.Bluetooth").
    /// </summary>
    string DriverId { get; }

    /// <summary>
    /// Evaluation priority. Lower numbers are evaluated first (e.g. 10 for vendor-specific, 100 for generic fallbacks).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Fast synchronous predicate to check if the driver handles the given device info. Does not perform I/O.
    /// </summary>
    bool Matches(DeviceInfo deviceInfo);

    /// <summary>
    /// Performs an isolated, read-only query to retrieve battery percentage and status.
    /// Must throw or return null on failure without swallowing cancellation.
    /// </summary>
    Task<BatteryReading?> TryReadAsync(DeviceInfo deviceInfo, CancellationToken cancellationToken);
}
