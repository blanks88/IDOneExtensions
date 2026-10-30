using Microsoft.Extensions.Logging.Abstractions;
using Syscom.Load;
using Syscom.Models;

namespace SyscomTestProject;

[Collection("Postgres sync")]
public class SyscomKnownCollisionTests
{
    private const string SingleModel = "FOAMY-EAR-CUSHION-FOR-UH34/YHS34-(1PCS)";
    private const string PackModel = "FOAMY-EAR-CUSHION-FOR-UH34/YHS34-(12-PCS)";

    [Fact]
    public void Normalization_KeepsEntireModelWithoutProductSpecificAliases()
    {
        Assert.Equal(SingleModel, SyscomDatabaseWorker.NormalizeCode("  " + SingleModel.ToLowerInvariant() + "  "));
        Assert.Equal(PackModel, SyscomDatabaseWorker.NormalizeCode(PackModel));
        Assert.Equal(new string('A', 400), SyscomDatabaseWorker.NormalizeCode(new string('a',400)));
    }

    [PostgresTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task KnownPair_PreservesExistingPackAndBothPrices(bool reverse, bool samePage)
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var uow = fixture.CreateUnitOfWork();
        var db = new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance, uow);
        var pack = SyscomPersistenceTests.Product(PackModel, 246653);
        var single = SyscomPersistenceTests.Product(SingleModel, 246645);
        single.Prices = new SyscomPrices { PrecioLista = "2" };
        var firstRun = await db.BeginRunAsync(30, CancellationToken.None);
        try
        {
            await db.BulkInsertAsync(firstRun, 26, [pack], CancellationToken.None);
            await db.CompleteDownloadAsync(firstRun, CancellationToken.None);
            await db.ExecSyncAsync(firstRun, 1, CancellationToken.None);
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
        var packId = await fixture.ScalarAsync("SELECT id FROM portfolio_products WHERE company_id=1");
        // Reproduce an existing legacy product without changing its ID or references.
        await using (var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var legacy = new Npgsql.NpgsqlCommand($"UPDATE portfolio_products SET code=left(code,35) WHERE id={packId}; INSERT INTO portfolio_prices(portfolio_price_list_id,portfolio_product_id,price) VALUES(12,{packId},99)", connection);
            await legacy.ExecuteNonQueryAsync();
        }
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var run = await db.BeginRunAsync(30, CancellationToken.None);
            try
            {
                List<SyscomProduct> products = reverse ? [pack, single] : [single, pack];
                if (samePage) await db.BulkInsertAsync(run, 26, products, CancellationToken.None);
                else foreach (var product in products)
                    await db.BulkInsertAsync(run, 26, [product], CancellationToken.None);
                await db.CompleteDownloadAsync(run, CancellationToken.None);
                await db.ExecSyncAsync(run, 1, CancellationToken.None);
            }
            finally { await db.ReleaseLockAsync(CancellationToken.None); }
            Assert.Equal(3, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products"));
            Assert.Equal(1, await fixture.ScalarAsync($"SELECT count(*) FROM portfolio_products WHERE id={packId} AND code='{PackModel}' AND price=10"));
            Assert.Equal(1, await fixture.ScalarAsync($"SELECT count(*) FROM portfolio_products WHERE code='{SingleModel}' AND price=2"));
            Assert.Equal(6, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id IN (10,11)"));
            Assert.Equal(1, await fixture.ScalarAsync($"SELECT count(*) FROM portfolio_prices p JOIN portfolio_products x ON x.id=p.portfolio_product_id WHERE x.code='{SingleModel}' AND p.portfolio_price_list_id=11 AND p.price=2.4"));
            Assert.Equal(1, await fixture.ScalarAsync($"SELECT count(*) FROM portfolio_prices WHERE portfolio_product_id={packId} AND portfolio_price_list_id=11 AND price=12"));
            Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id=12 AND price=9"));
            Assert.Equal(1, await fixture.ScalarAsync($"SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id=12 AND portfolio_product_id={packId} AND price=99"));
            Assert.Equal(0, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_missing"));
        }
    }
}
