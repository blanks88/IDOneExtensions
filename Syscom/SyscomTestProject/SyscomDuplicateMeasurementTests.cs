using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Syscom.Load;

namespace SyscomTestProject;

[Collection("Postgres sync")]
public class SyscomDuplicateMeasurementTests
{
    [PostgresFact]
    public async Task DuplicateSatUnits_PreserveExistingReferenceAndChooseOneForNewProducts()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = new NpgsqlCommand("""
                DROP INDEX measurement_unique;
                INSERT INTO catalogs_measurements(title,sat_key,store_type) VALUES
                    ('First','H87',0),('Second','H87',0),('Third','H87',0),('Other type','H87',1);
                INSERT INTO portfolio_products(title,code,company_id,currency_id,price,catalogs_measurement_id)
                    SELECT 'Existing','KEEP-UNIT',1,2,1,max(id) FROM catalogs_measurements WHERE store_type=0;
                """, connection);
            await seed.ExecuteNonQueryAsync();
        }
        var existingId = await fixture.ScalarAsync("SELECT id FROM portfolio_products WHERE code='KEEP-UNIT'");
        var existingUnit = await fixture.ScalarAsync("SELECT catalogs_measurement_id FROM portfolio_products WHERE code='KEEP-UNIT'");
        var canonicalUnit = await fixture.ScalarAsync("SELECT min(id) FROM catalogs_measurements WHERE store_type=0");
        await using var uow = fixture.CreateUnitOfWork();
        var db = new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance,uow);
        for (var iteration=0; iteration<2; iteration++)
        {
            var run = await db.BeginRunAsync(30,CancellationToken.None);
            try
            {
                await db.BulkInsertAsync(run,1,[SyscomPersistenceTests.Product("KEEP-UNIT",1),SyscomPersistenceTests.Product("NEW-UNIT",2)],CancellationToken.None);
                await db.CompleteDownloadAsync(run,CancellationToken.None);
                await db.ExecSyncAsync(run,1,CancellationToken.None);
            }
            finally { await db.ReleaseLockAsync(CancellationToken.None); }
            Assert.Equal(existingUnit,await fixture.ScalarAsync($"SELECT catalogs_measurement_id FROM portfolio_products WHERE id={existingId}"));
            Assert.Equal(canonicalUnit,await fixture.ScalarAsync("SELECT catalogs_measurement_id FROM portfolio_products WHERE code='NEW-UNIT'"));
            Assert.Equal(4,await fixture.ScalarAsync("SELECT count(*) FROM catalogs_measurements"));
            Assert.Equal(3,await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products"));
            Assert.Equal(6,await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id IN (10,11)"));
            Assert.Equal(1,await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id=12 AND price=9"));
        }
    }
}
