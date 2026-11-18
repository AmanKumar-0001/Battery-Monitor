namespace BatteryMonitor.History;

using System.Collections.Concurrent;
using System.IO;
using BatteryMonitor.Core.Models;
using Microsoft.Data.Sqlite;

public sealed class HistoryRepository : IAsyncDisposable, IDisposable
{
    private readonly string _connectionString;
    private readonly ConcurrentQueue<BatteryHistoryEntry> _writeQueue = new();
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private bool _isDisposed;

    public int QueuedCount => _writeQueue.Count;
    public string DatabasePath { get; }

    public HistoryRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;

        string? dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var walCmd = connection.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
        walCmd.ExecuteNonQuery();

        using var tableCmd = connection.CreateCommand();
        tableCmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS battery_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                device_id TEXT NOT NULL,
                timestamp INTEGER NOT NULL,
                percentage INTEGER NOT NULL,
                charging_state INTEGER NOT NULL,
                voltage REAL NULL
            );

            CREATE INDEX IF NOT EXISTS idx_history_device_timestamp 
            ON battery_history(device_id, timestamp DESC);";
        tableCmd.ExecuteNonQuery();
    }

    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var walCmd = connection.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
        await walCmd.ExecuteNonQueryAsync();

        await using var tableCmd = connection.CreateCommand();
        tableCmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS battery_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                device_id TEXT NOT NULL,
                timestamp INTEGER NOT NULL,
                percentage INTEGER NOT NULL,
                charging_state INTEGER NOT NULL,
                voltage REAL NULL
            );

            CREATE INDEX IF NOT EXISTS idx_history_device_timestamp 
            ON battery_history(device_id, timestamp DESC);";
        await tableCmd.ExecuteNonQueryAsync();
    }

    public void Enqueue(BatteryHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _writeQueue.Enqueue(entry);
    }

    public async Task FlushAsync()
    {
        if (_writeQueue.IsEmpty)
        {
            return;
        }

        await _flushLock.WaitAsync();
        try
        {
            var entriesToWrite = new List<BatteryHistoryEntry>();
            while (_writeQueue.TryDequeue(out var entry))
            {
                entriesToWrite.Add(entry);
            }

            if (entriesToWrite.Count == 0)
            {
                return;
            }

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO battery_history (device_id, timestamp, percentage, charging_state, voltage)
                VALUES (@device_id, @timestamp, @percentage, @charging_state, @voltage);";

            var pDeviceId = cmd.Parameters.Add("@device_id", SqliteType.Text);
            var pTimestamp = cmd.Parameters.Add("@timestamp", SqliteType.Integer);
            var pPercentage = cmd.Parameters.Add("@percentage", SqliteType.Integer);
            var pCharging = cmd.Parameters.Add("@charging_state", SqliteType.Integer);
            var pVoltage = cmd.Parameters.Add("@voltage", SqliteType.Real);

            foreach (var item in entriesToWrite)
            {
                pDeviceId.Value = item.DeviceId;
                pTimestamp.Value = item.Timestamp.ToUnixTimeSeconds();
                pPercentage.Value = item.Percentage;
                pCharging.Value = (int)item.ChargingState;
                pVoltage.Value = item.Voltage.HasValue ? item.Voltage.Value : DBNull.Value;

                await cmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        finally
        {
            _flushLock.Release();
        }
    }

    public async Task<int> PruneOldRecordsAsync(TimeSpan retention)
    {
        long cutoff = DateTimeOffset.UtcNow.Subtract(retention).ToUnixTimeSeconds();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM battery_history WHERE timestamp < @cutoff;";
        cmd.Parameters.AddWithValue("@cutoff", cutoff);

        int rowsDeleted = await cmd.ExecuteNonQueryAsync();

        if (rowsDeleted > 0)
        {
            try
            {
                await using var vacuumCmd = connection.CreateCommand();
                vacuumCmd.CommandText = "PRAGMA incremental_vacuum(100);";
                await vacuumCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Best effort vacuum
            }
        }

        return rowsDeleted;
    }

    public async Task<IReadOnlyList<BatteryHistoryEntry>> GetHistoryForDeviceAsync(
        string deviceId,
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        var list = new List<BatteryHistoryEntry>();
        long sinceUnix = since.ToUnixTimeSeconds();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, device_id, timestamp, percentage, charging_state, voltage
            FROM battery_history
            WHERE device_id = @device_id AND timestamp >= @since
            ORDER BY timestamp ASC;";
        cmd.Parameters.AddWithValue("@device_id", deviceId);
        cmd.Parameters.AddWithValue("@since", sinceUnix);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            long id = reader.GetInt64(0);
            string devId = reader.GetString(1);
            long tsUnix = reader.GetInt64(2);
            int pct = reader.GetInt32(3);
            var status = (ChargingStatus)reader.GetInt32(4);
            double? volt = reader.IsDBNull(5) ? null : reader.GetDouble(5);

            list.Add(new BatteryHistoryEntry(
                id,
                devId,
                DateTimeOffset.FromUnixTimeSeconds(tsUnix),
                pct,
                status,
                volt
            ));
        }

        return list;
    }

    public async Task<IReadOnlyList<BatteryHistoryEntry>> GetRecentHistoryAsync(
        string deviceId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var list = new List<BatteryHistoryEntry>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, device_id, timestamp, percentage, charging_state, voltage
            FROM battery_history
            WHERE device_id = @device_id
            ORDER BY timestamp DESC
            LIMIT @limit;";
        cmd.Parameters.AddWithValue("@device_id", deviceId);
        cmd.Parameters.AddWithValue("@limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            long id = reader.GetInt64(0);
            string devId = reader.GetString(1);
            long tsUnix = reader.GetInt64(2);
            int pct = reader.GetInt32(3);
            var status = (ChargingStatus)reader.GetInt32(4);
            double? volt = reader.IsDBNull(5) ? null : reader.GetDouble(5);

            list.Add(new BatteryHistoryEntry(
                id,
                devId,
                DateTimeOffset.FromUnixTimeSeconds(tsUnix),
                pct,
                status,
                volt
            ));
        }

        list.Reverse(); // Return in chronological order
        return list;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        FlushAsync().GetAwaiter().GetResult();
        _flushLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        await FlushAsync();
        _flushLock.Dispose();
    }
}
