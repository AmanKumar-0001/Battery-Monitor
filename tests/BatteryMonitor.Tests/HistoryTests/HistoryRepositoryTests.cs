namespace BatteryMonitor.Tests.HistoryTests;

using BatteryMonitor.Core.Models;
using BatteryMonitor.History;

public class HistoryRepositoryTests : IDisposable
{
    private readonly string _testDbPath;

    public HistoryRepositoryTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"BM_HistoryTest_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
            string shm = $"{_testDbPath}-shm";
            string wal = $"{_testDbPath}-wal";
            if (File.Exists(shm)) File.Delete(shm);
            if (File.Exists(wal)) File.Delete(wal);
        }
        catch
        {
        }
    }

    [Fact]
    public async Task InitializeAndBatchInsert_PersistsRecordsCorrectly()
    {
        await using var repo = new HistoryRepository(_testDbPath);
        await repo.InitializeAsync();

        var t0 = DateTimeOffset.UtcNow;
        repo.Enqueue(new BatteryHistoryEntry(0, "DEV_01", t0.AddMinutes(-10), 90, ChargingStatus.Discharging));
        repo.Enqueue(new BatteryHistoryEntry(0, "DEV_01", t0.AddMinutes(-5), 85, ChargingStatus.Discharging));
        repo.Enqueue(new BatteryHistoryEntry(0, "DEV_02", t0, 100, ChargingStatus.Charging));

        Assert.Equal(3, repo.QueuedCount);

        await repo.FlushAsync();
        Assert.Equal(0, repo.QueuedCount);

        var historyDev1 = await repo.GetHistoryForDeviceAsync("DEV_01", t0.AddMinutes(-15));
        Assert.Equal(2, historyDev1.Count);
        Assert.Equal(90, historyDev1[0].Percentage);
        Assert.Equal(85, historyDev1[1].Percentage);

        var historyDev2 = await repo.GetHistoryForDeviceAsync("DEV_02", t0.AddMinutes(-5));
        Assert.Single(historyDev2);
        Assert.Equal(100, historyDev2[0].Percentage);
        Assert.Equal(ChargingStatus.Charging, historyDev2[0].ChargingState);
    }

    [Fact]
    public async Task PruneOldRecords_DeletesAgedRecordsAndRetainsRecent()
    {
        await using var repo = new HistoryRepository(_testDbPath);
        await repo.InitializeAsync();

        var now = DateTimeOffset.UtcNow;

        // Old entry: 35 days ago (older than 30-day retention)
        repo.Enqueue(new BatteryHistoryEntry(0, "DEV_01", now.AddDays(-35), 50, ChargingStatus.Discharging));
        // Recent entry: 5 days ago
        repo.Enqueue(new BatteryHistoryEntry(0, "DEV_01", now.AddDays(-5), 75, ChargingStatus.Discharging));

        await repo.FlushAsync();

        int deleted = await repo.PruneOldRecordsAsync(TimeSpan.FromDays(30));
        Assert.Equal(1, deleted);

        var remaining = await repo.GetRecentHistoryAsync("DEV_01", 100);
        Assert.Single(remaining);
        Assert.Equal(75, remaining[0].Percentage);
    }
}
