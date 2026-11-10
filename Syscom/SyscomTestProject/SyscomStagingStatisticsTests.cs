using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Syscom.Load;

namespace SyscomTestProject;

[Collection("Postgres sync")]
public class SyscomStagingStatisticsTests
{
    [PostgresFact]
    public async Task CompletedDownload_PlansNewRunAtCatalogScale_AndPromotesMissingProducts()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var uow = fixture.CreateUnitOfWork();
        var db = new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance, uow);
        var previous = await db.BeginRunAsync(30, CancellationToken.None);
        try
        {
            await db.BulkInsertAsync(previous, 1, Enumerable.Range(1, 33000)
                .Select(i => SyscomPersistenceTests.Product($"MODEL-{i:D5}", i)).ToList(), CancellationToken.None);
            await db.CompleteDownloadAsync(previous, CancellationToken.None);
            await using var setup = new NpgsqlCommand("""
                ALTER TABLE syscom_sync_staging SET (autovacuum_enabled = false);
                UPDATE syscom_sync_runs SET status='Succeeded', stage='Complete', finished_at=now()
                WHERE run_id=@run;
                ANALYZE syscom_sync_staging;
                """, (NpgsqlConnection)uow.GetConnection());
            setup.Parameters.AddWithValue("run", previous);
            await setup.ExecuteNonQueryAsync();
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }

        var current = await db.BeginRunAsync(30, CancellationToken.None);
        try
        {
            await db.BulkInsertAsync(current, 1, Enumerable.Range(2, 33000)
                .Select(i => SyscomPersistenceTests.Product($"MODEL-{i:D5}", i)).ToList(), CancellationToken.None);
            await db.CompleteDownloadAsync(current, CancellationToken.None);
            await using var explain = new NpgsqlCommand("""
                EXPLAIN (FORMAT JSON) SELECT raw_model FROM syscom_sync_staging WHERE run_id=@run
                """, (NpgsqlConnection)uow.GetConnection());
            explain.Parameters.AddWithValue("run", current);
            using var plan = JsonDocument.Parse((string)(await explain.ExecuteScalarAsync())!);
            var estimatedRows = plan.RootElement[0].GetProperty("Plan").GetProperty("Plan Rows").GetInt64();
            Assert.True(estimatedRows > 10000, $"Planner estimated {estimatedRows} rows for a 33,000-row download.");
            await db.ExecSyncAsync(current, 1, CancellationToken.None);
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }

        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_missing WHERE code='MODEL-00001'"));
        Assert.Equal(33000, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products WHERE company_id=1"));
        Assert.Equal(1, await fixture.ScalarAsync($"SELECT count(*) FROM syscom_sync_runs WHERE run_id='{current}' AND status='Succeeded' AND products_missing=1 AND prices_replaced=66002"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id=12 AND price=9"));
    }
}
