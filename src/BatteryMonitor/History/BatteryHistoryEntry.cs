namespace BatteryMonitor.History;

using BatteryMonitor.Core.Models;

public sealed record BatteryHistoryEntry(
    long Id,
    string DeviceId,
    DateTimeOffset Timestamp,
    int Percentage,
    ChargingStatus ChargingState,
    double? Voltage = null
);
