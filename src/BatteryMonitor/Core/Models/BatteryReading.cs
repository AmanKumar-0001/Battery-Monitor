namespace BatteryMonitor.Core.Models;

public sealed record BatteryReading(
    int Percentage,
    ChargingStatus Status,
    bool IsAsleep = false,
    double? Voltage = null,
    DateTimeOffset Timestamp = default
)
{
    public DateTimeOffset Timestamp { get; init; } = Timestamp == default ? DateTimeOffset.UtcNow : Timestamp;
}
