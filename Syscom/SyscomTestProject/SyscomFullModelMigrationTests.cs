using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Syscom.Load;

namespace SyscomTestProject;

[Collection("Postgres sync")]
public class SyscomFullModelMigrationTests
{
    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AmbiguousLegacyIdentity_RollsBackAndDoesNotReplacePrices(bool titleMissing)
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        var prefix = new string('A',35);
        await Execute(fixture, $"INSERT INTO portfolio_products(title,code,company_id,currency_id,price) VALUES('{(titleMissing ? "Unknown" : "Shared")}', '{prefix}',1,2,19)");
        await using var uow = fixture.CreateUnitOfWork();
        var db = new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance,uow);
        var run = await db.BeginRunAsync(30,CancellationToken.None);
        try
        {
            var a = SyscomPersistenceTests.Product(prefix+"X",1); a.Titulo="Shared";
            var b = SyscomPersistenceTests.Product(prefix+"Y",2); b.Titulo="Shared";
            await db.BulkInsertAsync(run,26,[a,b],CancellationToken.None);
            await db.CompleteDownloadAsync(run,CancellationToken.None);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.ExecSyncAsync(run,1,CancellationToken.None));
            Assert.Contains("LegacyCodeIdentityAmbiguous",SyscomFailureDiagnostics.Describe(error));
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
        Assert.Equal(1,await fixture.ScalarAsync($"SELECT count(*) FROM portfolio_products WHERE code='{prefix}' AND price=19"));
        Assert.Equal(2,await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products"));
        Assert.Equal(1,await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id=10 AND price=7"));
    }

    [PostgresFact]
    public async Task PreviouslyAppliedAlias_IsRenamedWithoutChangingId()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        var full = new string('B',35)+"-COMPLETE";
        await Execute(fixture,$"INSERT INTO portfolio_products(title,code,company_id,currency_id,price) VALUES('{full}','SYSCOM-123',1,2,19)");
        var id = await fixture.ScalarAsync("SELECT id FROM portfolio_products WHERE code='SYSCOM-123'");
        await using var uow=fixture.CreateUnitOfWork();
        var db=new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance,uow);
        var run=await db.BeginRunAsync(30,CancellationToken.None);
        try
        {
            await db.BulkInsertAsync(run,26,[SyscomPersistenceTests.Product(full,123)],CancellationToken.None);
            await db.CompleteDownloadAsync(run,CancellationToken.None);
            await db.ExecSyncAsync(run,1,CancellationToken.None);
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
        Assert.Equal(1,await fixture.ScalarAsync($"SELECT count(*) FROM portfolio_products WHERE id={id} AND code='{full}'"));
        Assert.Equal(2,await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products"));
    }

    [PostgresFact]
    public async Task NarrowProductColumn_IsRejectedBeforeDownload()
    {
        await using var fixture=await PostgresSyncFixture.CreateAsync();
        await Execute(fixture,"ALTER TABLE portfolio_products ALTER COLUMN code TYPE varchar(35)");
        await using var uow=fixture.CreateUnitOfWork();
        var db=new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance,uow);
        try
        {
            var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>db.BeginRunAsync(30,CancellationToken.None));
            Assert.Contains("CodeSchemaMigrationRequired",SyscomFailureDiagnostics.Describe(error));
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
    }

    [PostgresFact]
    public async Task LegacyStagingColumns_AreWidenedWithoutDiscardingHistory()
    {
        await using var fixture=await PostgresSyncFixture.CreateAsync();
        await using var uow=fixture.CreateUnitOfWork();
        var db=new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance,uow);
        var first=await db.BeginRunAsync(30,CancellationToken.None);
        try { await db.BulkInsertAsync(first,1,[SyscomPersistenceTests.Product("HISTORY",1)],CancellationToken.None); }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
        await Execute(fixture,"ALTER TABLE syscom_sync_staging ALTER COLUMN code TYPE varchar(35); ALTER TABLE syscom_sync_missing ALTER COLUMN code TYPE varchar(35)");
        var next=await db.BeginRunAsync(30,CancellationToken.None);
        try { await db.BulkInsertAsync(next,1,[SyscomPersistenceTests.Product(new string('C',400),2)],CancellationToken.None); }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
        Assert.Equal(1,await fixture.ScalarAsync($"SELECT count(*) FROM syscom_sync_staging WHERE run_id='{first}' AND code='HISTORY'"));
        Assert.Equal(1,await fixture.ScalarAsync($"SELECT count(*) FROM syscom_sync_staging WHERE run_id='{next}' AND length(code)=400"));
    }

    [PostgresFact]
    public async Task PriceFailure_RollsBackLegacyCodeRenameAndPreservesReferences()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        var full = new string('D',35)+"-MODEL";
        await Execute(fixture,$"INSERT INTO portfolio_products(title,code,company_id,currency_id,price) VALUES('{full}',left('{full}',35),1,2,19)");
        var id = await fixture.ScalarAsync("SELECT id FROM portfolio_products WHERE company_id=1");
        await Execute(fixture,$"CREATE TABLE retained_reference(product_id bigint REFERENCES portfolio_products(id)); INSERT INTO retained_reference VALUES({id}); CREATE FUNCTION reject_prices() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'fixture price failure'; END $$; CREATE TRIGGER reject_prices BEFORE INSERT ON portfolio_prices FOR EACH ROW EXECUTE FUNCTION reject_prices()");
        await using var uow=fixture.CreateUnitOfWork();
        var db=new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance,uow);
        var run=await db.BeginRunAsync(30,CancellationToken.None);
        try
        {
            await db.BulkInsertAsync(run,26,[SyscomPersistenceTests.Product(full,42)],CancellationToken.None);
            await db.CompleteDownloadAsync(run,CancellationToken.None);
            await Assert.ThrowsAsync<PostgresException>(()=>db.ExecSyncAsync(run,1,CancellationToken.None));
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
        Assert.Equal(1,await fixture.ScalarAsync($"SELECT count(*) FROM portfolio_products WHERE id={id} AND code=left('{full}',35) AND price=19"));
        Assert.Equal(id,await fixture.ScalarAsync("SELECT product_id FROM retained_reference"));
        Assert.Equal(1,await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id=10 AND price=7"));
    }

    [PostgresFact]
    public async Task ExistingFullCode_DoesNotCauseTwoIdsToMerge()
    {
        await using var fixture=await PostgresSyncFixture.CreateAsync();
        var full=new string('E',35)+"-MODEL";
        await Execute(fixture,$"INSERT INTO portfolio_products(title,code,company_id,currency_id,price) VALUES('{full}',left('{full}',35),1,2,19),('{full}','{full}',1,2,21)");
        await using var uow=fixture.CreateUnitOfWork();
        var db=new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance,uow);
        var run=await db.BeginRunAsync(30,CancellationToken.None);
        try
        {
            await db.BulkInsertAsync(run,26,[SyscomPersistenceTests.Product(full,42)],CancellationToken.None);
            await db.CompleteDownloadAsync(run,CancellationToken.None);
            var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>db.ExecSyncAsync(run,1,CancellationToken.None));
            Assert.Contains("LegacyCodeIdentityAmbiguous",SyscomFailureDiagnostics.Describe(error));
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
        Assert.Equal(2,await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products WHERE company_id=1"));
    }

    [PostgresFact]
    public async Task UniqueLegacyCandidate_WithChangedTitle_PreservesIdAndReferences()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        const string full = "BH76-PLUS-WITH-CHARGING-STAND-TEAMS-BLACK-USB-C/A";
        await Execute(fixture,$"INSERT INTO portfolio_products(title,code,company_id,currency_id,price) VALUES('Old description',left('{full}',35),1,2,19)");
        var id = await fixture.ScalarAsync("SELECT id FROM portfolio_products WHERE company_id=1");
        await Execute(fixture,$"CREATE TABLE preserved_reference(product_id bigint REFERENCES portfolio_products(id)); INSERT INTO preserved_reference VALUES({id})");
        await using var uow = fixture.CreateUnitOfWork();
        var db = new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance,uow);
        var run = await db.BeginRunAsync(30,CancellationToken.None);
        try
        {
            var product = SyscomPersistenceTests.Product(full,246322);
            product.Titulo = "New description";
            await db.BulkInsertAsync(run,26,[product],CancellationToken.None);
            await db.CompleteDownloadAsync(run,CancellationToken.None);
            await db.ExecSyncAsync(run,1,CancellationToken.None);
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
        Assert.Equal(1,await fixture.ScalarAsync($"SELECT count(*) FROM portfolio_products WHERE id={id} AND code='{full}' AND title='New description'"));
        Assert.Equal(id,await fixture.ScalarAsync("SELECT product_id FROM preserved_reference"));
        Assert.Equal(2,await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products"));
        Assert.Equal(4,await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id IN (10,11)"));
    }

    private static async Task Execute(PostgresSyncFixture fixture,string sql)
    {
        await using var connection=new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command=new NpgsqlCommand(sql,connection);
        await command.ExecuteNonQueryAsync();
    }
}
