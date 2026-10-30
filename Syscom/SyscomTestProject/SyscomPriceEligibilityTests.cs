using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Syscom.Load;
using Syscom.Models;

namespace SyscomTestProject;

[Collection("Postgres sync")]
public class SyscomPriceEligibilityTests
{
    [PostgresFact]
    public async Task InvalidSourcePrices_AreSkippedWithoutDeletingHistoricalProductsOrFixedPrices()
    {
        await using var fixture=await PostgresSyncFixture.CreateAsync();
        await Execute(fixture,"""
            INSERT INTO portfolio_products(code,company_id,currency_id,price) VALUES
              ('OLD-NULL',1,2,NULL),('OLD-ZERO',2,2,0),('OLD-NEGATIVE',1,2,-1000);
            INSERT INTO portfolio_prices(portfolio_price_list_id,portfolio_product_id,price)
              SELECT l.id,p.id,99 FROM portfolio_products p CROSS JOIN portfolio_price_lists l WHERE p.code LIKE 'OLD-%';
            """);
        await using var uow=fixture.CreateUnitOfWork();
        var db=new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance,uow);
        var run=await db.BeginRunAsync(30,CancellationToken.None);
        try
        {
            var absent=SyscomPersistenceTests.Product("NEW-NULL",1); absent.Prices=new SyscomPrices();
            var zero=SyscomPersistenceTests.Product("NEW-ZERO",2); zero.Prices=new SyscomPrices { PrecioLista="0" };
            var negative=SyscomPersistenceTests.Product("NEW-NEGATIVE",3); negative.Prices=new SyscomPrices { PrecioLista="-5" };
            await db.BulkInsertAsync(run,1,[absent,zero,negative,SyscomPersistenceTests.Product("NEW-VALID",4)],CancellationToken.None);
            await db.CompleteDownloadAsync(run,CancellationToken.None);
            await db.ExecSyncAsync(run,1,CancellationToken.None);
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
        Assert.Equal(1,await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_runs WHERE products_seen=4 AND products_skipped=3 AND products_staged=1 AND prices_replaced=4"));
        Assert.Equal(3,await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products WHERE code LIKE 'OLD-%'"));
        Assert.Equal(1,await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products WHERE code LIKE 'NEW-%'"));
        Assert.Equal(0,await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices pp JOIN portfolio_products p ON p.id=pp.portfolio_product_id WHERE p.code LIKE 'OLD-%' AND pp.portfolio_price_list_id IN (10,11)"));
        Assert.Equal(4,await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id=12"));
    }

    [PostgresFact]
    public async Task SecondaryCurrencyConversion_UsesBothConfiguredRatesOnEveryRun()
    {
        await using var fixture=await PostgresSyncFixture.CreateAsync();
        await Execute(fixture,"""
            INSERT INTO portfolio_products(code,company_id,currency_id,price) VALUES('EUR-PRODUCT',2,3,10);
            INSERT INTO portfolio_price_lists(id,currency_id,percent_reference) VALUES(13,3,10);
            """);
        await using var uow=fixture.CreateUnitOfWork();
        var db=new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance,uow);
        for(var iteration=0;iteration<2;iteration++)
        {
            if(iteration==1) await Execute(fixture,"UPDATE exchange_rates SET exchange_rate=25 WHERE currency_id=3");
            var run=await db.BeginRunAsync(30,CancellationToken.None);
            try
            {
                await db.BulkInsertAsync(run,1,[SyscomPersistenceTests.Product("USD-PRODUCT",1)],CancellationToken.None);
                await db.CompleteDownloadAsync(run,CancellationToken.None);
                await db.ExecSyncAsync(run,1,CancellationToken.None);
            }
            finally { await db.ReleaseLockAsync(CancellationToken.None); }
            var euroInUsd=iteration==0 ? "13.2" : "15";
            var usdInEuro=iteration==0 ? "10" : "8.8";
            Assert.Equal(1,await fixture.ScalarAsync($"SELECT count(*) FROM portfolio_prices pp JOIN portfolio_products p ON p.id=pp.portfolio_product_id WHERE p.code='EUR-PRODUCT' AND pp.portfolio_price_list_id=11 AND pp.price={euroInUsd}"));
            Assert.Equal(1,await fixture.ScalarAsync($"SELECT count(*) FROM portfolio_prices pp JOIN portfolio_products p ON p.id=pp.portfolio_product_id WHERE p.code='USD-PRODUCT' AND pp.portfolio_price_list_id=13 AND pp.price={usdInEuro}"));
            Assert.Equal(9,await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id IN (10,11,13)"));
        }
    }

    private static async Task Execute(PostgresSyncFixture fixture,string sql)
    {
        await using var connection=new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command=new NpgsqlCommand(sql,connection);
        await command.ExecuteNonQueryAsync();
    }
}
