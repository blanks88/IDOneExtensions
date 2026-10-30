using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Syscom.Load;

namespace SyscomTestProject;

[Collection("Postgres sync")]
public class SyscomNullableCurrencyTests
{
    [PostgresFact]
    public async Task NullPrincipalFlag_OnExistingCurrency_IsSecondary()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = new NpgsqlCommand("ALTER TABLE currencies ALTER COLUMN principal DROP NOT NULL; UPDATE currencies SET principal=NULL WHERE id IN (2,3)",connection);
            await seed.ExecuteNonQueryAsync();
        }
        await using var uow = fixture.CreateUnitOfWork();
        var db = new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance,uow);
        var run = await db.BeginRunAsync(30,CancellationToken.None);
        try
        {
            await db.BulkInsertAsync(run,1,[SyscomPersistenceTests.Product("USD-PRODUCT",1)],CancellationToken.None);
            await db.CompleteDownloadAsync(run,CancellationToken.None);
            await db.ExecSyncAsync(run,1,CancellationToken.None);
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
        Assert.Equal(1,await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices p JOIN portfolio_products x ON x.id=p.portfolio_product_id WHERE x.code='USD-PRODUCT' AND p.portfolio_price_list_id=10 AND p.price=220"));
        Assert.Equal(1,await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices p JOIN portfolio_products x ON x.id=p.portfolio_product_id WHERE x.code='OTHER-2' AND p.portfolio_price_list_id=11 AND p.price=6"));
        Assert.Equal(4,await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id IN (10,11)"));
    }
}
