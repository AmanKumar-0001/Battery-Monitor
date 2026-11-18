namespace BatteryMonitor.History;

using BatteryMonitor.Core.Models;

public enum EstimateConfidence
{
    InsufficientData,
    Low,
    Medium,
    High,
    Charging
}

public sealed record DischargeEstimate(
    TimeSpan? TimeRemaining,
    EstimateConfidence Confidence,
    double? DischargeRatePercentPerHour,
    string FormattedEstimate
);

public static class DischargeEstimator
{
    public static DischargeEstimate EstimateTimeRemaining(IReadOnlyList<BatteryHistoryEntry> history)
    {
        if (history == null || history.Count == 0)
        {
            return new DischargeEstimate(null, EstimateConfidence.InsufficientData, null, "Calculating... (No data)");
        }

        var latest = history[^1];
        if (latest.ChargingState is ChargingStatus.Charging or ChargingStatus.Full or ChargingStatus.SlowCharge)
        {
            return new DischargeEstimate(null, EstimateConfidence.Charging, null, "Plugged In / Charging");
        }

        // Take only recent discharging records from current discharge cycle
        var dischargingEntries = history
            .Where(e => e.ChargingState == ChargingStatus.Discharging)
            .ToList();

        if (dischargingEntries.Count < 2)
        {
            return new DischargeEstimate(null, EstimateConfidence.InsufficientData, null, "Calculating... (Need more data)");
        }

        DateTimeOffset t0 = dischargingEntries[0].Timestamp;
        double totalSpanHours = (dischargingEntries[^1].Timestamp - t0).TotalHours;

        if (totalSpanHours < (3.0 / 60.0)) // Less than 3 minutes of history
        {
            return new DischargeEstimate(null, EstimateConfidence.InsufficientData, null, "Calculating... (Accumulating data)");
        }

        // Simple Linear Regression: y = m*x + c
        int n = dischargingEntries.Count;
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;

        foreach (var entry in dischargingEntries)
        {
            double x = (entry.Timestamp - t0).TotalHours;
            double y = entry.Percentage;

            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumX2 += x * x;
        }

        double denominator = (n * sumX2) - (sumX * sumX);
        if (Math.Abs(denominator) < 1e-9)
        {
            return new DischargeEstimate(null, EstimateConfidence.InsufficientData, null, "Calculating...");
        }

        double slope = ((n * sumXY) - (sumX * sumY)) / denominator; // % change per hour

        // If slope is flat or positive
        if (slope >= 0)
        {
            return new DischargeEstimate(null, EstimateConfidence.Low, 0, "Stable (Discharge rate minimal)");
        }

        double dischargeRatePerHour = -slope; // positive % per hour
        double currentPct = latest.Percentage;
        double hoursRemaining = currentPct / dischargeRatePerHour;

        if (hoursRemaining > 500) // Unreasonably large estimate
        {
            return new DischargeEstimate(null, EstimateConfidence.Low, dischargeRatePerHour, "Stable (> 500h)");
        }

        var timeRemaining = TimeSpan.FromHours(hoursRemaining);

        EstimateConfidence confidence;
        if (n >= 10 && totalSpanHours >= 1.0)
        {
            confidence = EstimateConfidence.High;
        }
        else if (n >= 5 && totalSpanHours >= 0.3)
        {
            confidence = EstimateConfidence.Medium;
        }
        else
        {
            confidence = EstimateConfidence.Low;
        }

        string formatted;
        if (timeRemaining.TotalHours >= 24)
        {
            int days = (int)timeRemaining.TotalDays;
            int hours = timeRemaining.Hours;
            formatted = $"Est. {days}d {hours}h remaining ({confidence})";
        }
        else
        {
            int hours = (int)timeRemaining.TotalHours;
            int mins = timeRemaining.Minutes;
            formatted = $"Est. {hours}h {mins}m remaining ({confidence})";
        }

        return new DischargeEstimate(
            timeRemaining,
            confidence,
            Math.Round(dischargeRatePerHour, 2),
            formatted
        );
    }
}
