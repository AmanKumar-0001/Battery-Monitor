namespace BatteryMonitor.History;

using System.IO;
using System.Text;

public static class CsvExporter
{
    public static void ExportToCsv(
        string destinationFilePath,
        IReadOnlyList<BatteryHistoryEntry> entries,
        string? deviceName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);
        ArgumentNullException.ThrowIfNull(entries);

        string? dir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var writer = new StreamWriter(destinationFilePath, false, Encoding.UTF8);
        ExportToWriter(writer, entries, deviceName);
    }

    public static void ExportToWriter(
        TextWriter writer,
        IReadOnlyList<BatteryHistoryEntry> entries,
        string? deviceName = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(entries);

        writer.WriteLine("TimestampUtc,DeviceId,DeviceName,Percentage,ChargingState,Voltage");

        foreach (var entry in entries)
        {
            string devName = EscapeCsv(deviceName ?? entry.DeviceId);
            string devId = EscapeCsv(entry.DeviceId);
            string timestamp = entry.Timestamp.UtcDateTime.ToString("o");
            string pct = entry.Percentage.ToString();
            string state = entry.ChargingState.ToString();
            string volt = entry.Voltage.HasValue ? entry.Voltage.Value.ToString("F2") : "";

            writer.WriteLine($"{timestamp},{devId},{devName},{pct},{state},{volt}");
        }
    }

    private static string EscapeCsv(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}
