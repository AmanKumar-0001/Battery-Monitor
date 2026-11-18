namespace BatteryMonitor.Tests.HistoryTests;

using BatteryMonitor.Core.Models;
using BatteryMonitor.History;

public class DischargeEstimatorTests
{
    [Fact]
    public void Estimate_InsufficientData_ReturnsAppropriateConfidence()
    {
        // Empty history
        var empty = DischargeEstimator.EstimateTimeRemaining(Array.Empty<BatteryHistoryEntry>());
        Assert.Equal(EstimateConfidence.InsufficientData, empty.Confidence);
        Assert.Null(empty.TimeRemaining);

        // Single entry
        var single = new[]
        {
            new BatteryHistoryEntry(1, "DEV_01", DateTimeOffset.UtcNow, 80, ChargingStatus.Discharging)
        };
        var resSingle = DischargeEstimator.EstimateTimeRemaining(single);
        Assert.Equal(EstimateConfidence.InsufficientData, resSingle.Confidence);
    }

    [Fact]
    public void Estimate_ChargingState_ReturnsChargingStatus()
    {
        var entries = new[]
        {
            new BatteryHistoryEntry(1, "DEV_01", DateTimeOffset.UtcNow.AddMinutes(-10), 40, ChargingStatus.Discharging),
            new BatteryHistoryEntry(2, "DEV_01", DateTimeOffset.UtcNow, 45, ChargingStatus.Charging)
        };

        var res = DischargeEstimator.EstimateTimeRemaining(entries);
        Assert.Equal(EstimateConfidence.Charging, res.Confidence);
        Assert.Null(res.TimeRemaining);
        Assert.Contains("Charging", res.FormattedEstimate);
    }

    [Fact]
    public void Estimate_SteadyDischarge_CalculatesAccurateTimeRemaining()
    {
        // 10 data points over 2 hours discharging from 100% to 90% (rate = 5% per hour)
        var t0 = DateTimeOffset.UtcNow.AddHours(-2);
        var entries = new List<BatteryHistoryEntry>();

        for (int i = 0; i <= 10; i++)
        {
            // Every 12 minutes drops 1%
            var t = t0.AddMinutes(i * 12);
            int pct = 100 - i;
            entries.Add(new BatteryHistoryEntry(i, "DEV_01", t, pct, ChargingStatus.Discharging));
        }

        var res = DischargeEstimator.EstimateTimeRemaining(entries);

        Assert.Equal(EstimateConfidence.High, res.Confidence);
        Assert.NotNull(res.TimeRemaining);
        Assert.NotNull(res.DischargeRatePercentPerHour);

        // Rate is ~5% per hour. Current battery is 90%. 90 / 5 = 18 hours remaining.
        Assert.Equal(5.0, res.DischargeRatePercentPerHour.Value, 0.5);
        Assert.Equal(18.0, res.TimeRemaining.Value.TotalHours, 1.5);
    }
}

public class CsvExporterTests
{
    [Fact]
    public void CsvExporter_GeneratesValidCsvWithHeadersAndEscaping()
    {
        var t0 = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);
        var entries = new[]
        {
            new BatteryHistoryEntry(1, "DEV_01", t0, 85, ChargingStatus.Discharging, 3.85),
            new BatteryHistoryEntry(2, "DEV_01", t0.AddMinutes(5), 84, ChargingStatus.Discharging, 3.84)
        };

        using var sw = new StringWriter();
        CsvExporter.ExportToWriter(sw, entries, "Logitech Mouse, \"Pro\"");

        string csv = sw.ToString();
        var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.Equal("TimestampUtc,DeviceId,DeviceName,Percentage,ChargingState,Voltage", lines[0]);

        // Line 1 should escape commas and quotes in device name
        Assert.Contains("\"Logitech Mouse, \"\"Pro\"\"\"", lines[1]);
        Assert.Contains("85", lines[1]);
        Assert.Contains("3.85", lines[1]);
    }
}
