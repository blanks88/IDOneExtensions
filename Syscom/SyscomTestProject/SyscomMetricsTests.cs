using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Syscom.Load;

namespace SyscomTestProject;

[Collection("Postgres sync")]
public sealed class SyscomMetricsTests
{
    [Fact]
    public void Measure_UsesMonotonicClockAndAccumulatesRepeatedStageCalls()
    {
        var clock = new ManualTimeProvider();
        var metrics = new SyscomSyncMetrics(clock);
        using (metrics.Measure(SyscomSyncStage.Download)) clock.AdvanceMilliseconds(12);
        clock.AdvanceMilliseconds(3);
        using (metrics.Measure(SyscomSyncStage.Download)) clock.AdvanceMilliseconds(7);

        Assert.Equal(19, metrics.GetElapsedMilliseconds(SyscomSyncStage.Download));
        Assert.Equal(22, metrics.TotalElapsedMilliseconds);
        using var json = JsonDocument.Parse(metrics.ToJson());
        Assert.Equal(19, json.RootElement.GetProperty("download_ms").GetInt64());
        Assert.Equal(22, json.RootElement.GetProperty("total_elapsed_ms").GetInt64());
        Assert.Equal(0, json.RootElement.GetProperty("preparation_ms").GetInt64());
    }

    [Fact]
    public void Measure_NestedAndOverlappingScopesAccumulateIndependentlyAndDisposeOnce()
    {
        var clock = new ManualTimeProvider();
        var metrics = new SyscomSyncMetrics(clock);
        var promotion = metrics.Measure(SyscomSyncStage.Promotion);
        clock.AdvanceMilliseconds(2);
        var catalog = metrics.Measure(SyscomSyncStage.CatalogPromotion);
        var firstStaging = metrics.Measure(SyscomSyncStage.Staging);
        var secondStaging = metrics.Measure(SyscomSyncStage.Staging);
        clock.AdvanceMilliseconds(5);
        Assert.Equal(10, metrics.GetElapsedMilliseconds(SyscomSyncStage.Staging));
        Assert.Equal(7, metrics.GetElapsedMilliseconds(SyscomSyncStage.Promotion));
        firstStaging.Dispose();
        clock.AdvanceMilliseconds(3);
        secondStaging.Dispose();
        secondStaging.Dispose();
        catalog.Dispose();
        promotion.Dispose();

        Assert.Equal(13, metrics.GetElapsedMilliseconds(SyscomSyncStage.Staging));
        Assert.Equal(8, metrics.GetElapsedMilliseconds(SyscomSyncStage.CatalogPromotion));
        Assert.Equal(10, metrics.GetElapsedMilliseconds(SyscomSyncStage.Promotion));
        Assert.Equal(10, metrics.TotalElapsedMilliseconds);
    }

    [Fact]
    public async Task Measure_ConcurrentScopesDisposeWithoutLostTime()
    {
        var clock = new ManualTimeProvider();
        var metrics = new SyscomSyncMetrics(clock);
        const int scopeCount = 32;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var tasks = Enumerable.Range(0, scopeCount).Select(async _ =>
        {
            using (metrics.Measure(SyscomSyncStage.Download))
            {
                if (Interlocked.Increment(ref started) == scopeCount) ready.SetResult();
                await release.Task;
            }
        }).ToArray();
        await ready.Task;
        clock.AdvanceMilliseconds(4);
        release.SetResult();
        await Task.WhenAll(tasks);

        Assert.Equal(scopeCount * 4, metrics.GetElapsedMilliseconds(SyscomSyncStage.Download));
        Assert.Equal(4, metrics.TotalElapsedMilliseconds);
    }

    [Fact]
    public void Measure_ExceptionStillRecordsElapsedStageTime()
    {
        var clock = new ManualTimeProvider();
        var metrics = new SyscomSyncMetrics(clock);
        void FailPriceRecalculation()
        {
            using (metrics.Measure(SyscomSyncStage.PriceRecalculation))
            {
                clock.AdvanceMilliseconds(17);
                throw new InvalidOperationException("simulated validation failure");
            }
        }
        Assert.Throws<InvalidOperationException>(FailPriceRecalculation);
        Assert.Equal(17, metrics.GetElapsedMilliseconds(SyscomSyncStage.PriceRecalculation));
    }

    [PostgresFact]
    public async Task RecordMetrics_UpgradesLegacyRunTableAndPreservesFailedState()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var legacy = new NpgsqlCommand("""
                CREATE TABLE syscom_sync_runs (
                    run_id uuid PRIMARY KEY, company_id bigint NOT NULL DEFAULT 1,
                    status text NOT NULL, stage text NOT NULL, finished_at timestamptz);
                """, connection);
            await legacy.ExecuteNonQueryAsync();
        }

        var clock = new ManualTimeProvider();
        var metrics = new SyscomSyncMetrics(clock);
        await using var uow = fixture.CreateUnitOfWork();
        var db = new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance, uow, metrics);
        var run = await db.BeginRunAsync(30, CancellationToken.None);
        try
        {
            using (metrics.Measure(SyscomSyncStage.Download)) clock.AdvanceMilliseconds(14);
            await db.FailRunAsync(run, "Failed", CancellationToken.None);
            await db.RecordMetricsAsync(run, CancellationToken.None);
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }

        await using var read = new NpgsqlConnection(fixture.ConnectionString);
        await read.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT status, stage, finished_at IS NOT NULL, metrics::text FROM syscom_sync_runs WHERE run_id=@run", read);
        command.Parameters.AddWithValue("run", run);
        await using var row = await command.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal("Failed", row.GetString(0));
        Assert.Equal("Downloading", row.GetString(1));
        Assert.True(row.GetBoolean(2));
        using var json = JsonDocument.Parse(row.GetString(3));
        Assert.Equal(14, json.RootElement.GetProperty("download_ms").GetInt64());
        Assert.Equal(14, json.RootElement.GetProperty("total_elapsed_ms").GetInt64());
        Assert.False(await row.ReadAsync());
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public void AdvanceMilliseconds(long milliseconds) =>
            Interlocked.Add(ref _timestamp, milliseconds * TimeSpan.TicksPerMillisecond);
    }
}
