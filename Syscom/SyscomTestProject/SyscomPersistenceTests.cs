using IDOneRepository;
using IDOneRepository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using Syscom;
using Syscom.Load;
using Syscom.Models;

namespace SyscomTestProject;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SYSCOM_TEST_DB_CONNECTION")))
            Skip = "Set SYSCOM_TEST_DB_CONNECTION to opt into disposable local PostgreSQL tests.";
    }
}

public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SYSCOM_TEST_DB_CONNECTION")))
            Skip = "Set SYSCOM_TEST_DB_CONNECTION to opt into disposable local PostgreSQL tests.";
    }
}

[CollectionDefinition("Postgres sync", DisableParallelization = true)]
public sealed class PostgresSyncCollection;

public sealed class PostgresSyncFixture : IAsyncDisposable
{
    private readonly string _databaseConnection;
    public string Schema { get; }
    public string ConnectionString { get; }

    private PostgresSyncFixture(string databaseConnection, string schema)
    {
        _databaseConnection = databaseConnection;
        Schema = schema;
        ConnectionString = new NpgsqlConnectionStringBuilder(databaseConnection) { SearchPath = schema }.ConnectionString;
    }

    public static async Task<PostgresSyncFixture> CreateAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("SYSCOM_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("Set SYSCOM_TEST_DB_CONNECTION for disposable local PostgreSQL tests.");
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        if (parsed.Host is not ("127.0.0.1" or "localhost" or "::1") ||
            parsed.Database != "idone_sync_tests")
            throw new InvalidOperationException("SYSCOM_TEST_DB_CONNECTION must target the local idone_sync_tests database.");
        var fixture = new PostgresSyncFixture(connectionString, "sync_" + Guid.NewGuid().ToString("N"));
        await using var admin = new NpgsqlConnection(connectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {fixture.Schema}", admin))
            await create.ExecuteNonQueryAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var schema = new NpgsqlCommand(SchemaSql, connection);
        await schema.ExecuteNonQueryAsync();
        return fixture;
    }

    public UnitOfWork CreateUnitOfWork() => new(new IDOneDbContext(
        new DbContextOptionsBuilder<IDOneDbContext>().UseNpgsql(ConnectionString).Options));

    public async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async ValueTask DisposeAsync()
    {
        await using var admin = new NpgsqlConnection(_databaseConnection);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {Schema} CASCADE", admin);
        await drop.ExecuteNonQueryAsync();
    }

    private const string SchemaSql = """
        CREATE TABLE catalogs_sat_products (id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            code varchar(14) NOT NULL UNIQUE, title varchar(350), created_at timestamptz, updated_at timestamptz);
        CREATE TABLE catalogs_measurements (id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            title varchar(70), sat_description varchar(350), sat_key varchar(14), store_type int,
            created_at timestamptz, updated_at timestamptz);
        CREATE UNIQUE INDEX measurement_unique ON catalogs_measurements(sat_key,store_type);
        CREATE TABLE currencies (id bigint PRIMARY KEY, principal boolean NOT NULL);
        CREATE TABLE exchange_rates (id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            currency_id bigint NOT NULL, principal boolean NOT NULL, exchange_rate numeric(14,4) NOT NULL);
        CREATE TABLE portfolio_products (id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            title varchar(350), code varchar(35), catalogs_measurement_id bigint, details text,
            parent_id bigint, created_at timestamptz, updated_at timestamptz, company_id bigint NOT NULL,
            currency_id bigint, price numeric(14,4), trade_mark varchar(70),
            catalogs_sat_product_id bigint, pr_quantity numeric(14,4), UNIQUE(company_id,code));
        CREATE TABLE portfolio_price_lists (id bigint PRIMARY KEY, currency_id bigint,
            percent_reference numeric(14,4));
        CREATE TABLE portfolio_prices (id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            portfolio_price_list_id bigint, portfolio_product_id bigint, price numeric(14,4),
            created_at timestamptz, updated_at timestamptz, auto_update boolean);
        INSERT INTO currencies(id,principal) VALUES(1,true),(2,false),(3,false);
        INSERT INTO exchange_rates(currency_id,principal,exchange_rate)
            VALUES(1,true,1),(2,true,20),(3,true,22);
        INSERT INTO portfolio_price_lists(id,currency_id,percent_reference)
            VALUES(10,1,10),(11,2,20),(12,3,NULL);
        INSERT INTO portfolio_products(title,code,company_id,currency_id,price,pr_quantity)
            VALUES('Other company','OTHER-2',2,1,100,1);
        INSERT INTO portfolio_prices(portfolio_price_list_id,portfolio_product_id,price,auto_update)
            VALUES(10,1,7,false),(11,1,8,false),(12,1,9,false);
        """;
}

[Collection("Postgres sync")]
public class SyscomPersistenceTests
{
    [PostgresFact]
    public async Task Promotion_ReplacesGlobalPercentagePrices_AndPreservesFixedList()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var uow = fixture.CreateUnitOfWork();
        var db = new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance, uow);
        var run = await db.BeginRunAsync(30, CancellationToken.None);
        try
        {
            await db.BulkInsertAsync(run, 1, [Product("MODEL-1", 10)], CancellationToken.None);
            await db.CompleteDownloadAsync(run, CancellationToken.None);
            await db.ExecSyncAsync(run, 1, CancellationToken.None);
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
        Assert.Equal(4, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id IN (10,11)"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id=12 AND price=9"));
        Assert.Equal(2, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices p JOIN portfolio_products x ON x.id=p.portfolio_product_id WHERE x.company_id=2 AND p.portfolio_price_list_id IN (10,11)"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_runs WHERE status='Succeeded' AND prices_replaced=4"));
    }

    [PostgresFact]
    public async Task MissingExchangeRate_RollsBackProductsAndPrices()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("DELETE FROM exchange_rates WHERE currency_id=2", connection);
            await command.ExecuteNonQueryAsync();
        }
        await using var uow = fixture.CreateUnitOfWork();
        var db = new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance, uow);
        var run = await db.BeginRunAsync(30, CancellationToken.None);
        try
        {
            await db.BulkInsertAsync(run, 1, [Product("MODEL-1", 10)], CancellationToken.None);
            await db.CompleteDownloadAsync(run, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.ExecSyncAsync(run, 1, CancellationToken.None));
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
        Assert.Equal(0, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products WHERE code='MODEL-1'"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id=10 AND price=7"));
    }

    [PostgresFact]
    public async Task NormalizedCodeCollision_RejectsSecondPage()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var uow = fixture.CreateUnitOfWork();
        var db = new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance, uow);
        var run = await db.BeginRunAsync(30, CancellationToken.None);
        try
        {
            var prefix = new string('A', 35);
            await db.BulkInsertAsync(run, 1, [Product(prefix + "X", 10)], CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.BulkInsertAsync(run, 2,
                [Product(prefix + "Y", 10)], CancellationToken.None));
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_staging"));
    }

    [PostgresFact]
    public async Task NormalizedCodeCollision_WithinPageRollsBackEntirePage()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var uow = fixture.CreateUnitOfWork();
        var db = new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance, uow);
        var run = await db.BeginRunAsync(30, CancellationToken.None);
        try
        {
            var prefix = new string('B', 35);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.BulkInsertAsync(run, 1,
                [Product(prefix + "X", 1), Product(prefix + "Y", 2)], CancellationToken.None));
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
        Assert.Equal(0, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_staging"));
        Assert.Equal(0, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_runs WHERE pages_loaded<>0"));
    }

    [PostgresFact]
    public async Task AdvisoryLock_RejectsConcurrentRun()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var firstUow = fixture.CreateUnitOfWork();
        await using var secondUow = fixture.CreateUnitOfWork();
        var first = new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance, firstUow);
        var second = new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance, secondUow);
        await first.BeginRunAsync(30, CancellationToken.None);
        try
        {
            await Assert.ThrowsAsync<SyscomSyncAlreadyRunningException>(() =>
                second.BeginRunAsync(30, CancellationToken.None));
            Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_runs"));
        }
        finally { await first.ReleaseLockAsync(CancellationToken.None); }
    }

    [PostgresFact]
    public async Task IncompleteRun_CannotPromote_AndNextRunDoesNotReuseStaging()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var uow = fixture.CreateUnitOfWork();
        var db = new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance, uow);
        var first = await db.BeginRunAsync(30, CancellationToken.None);
        await db.BulkInsertAsync(first, 1, [Product("OLD-PARTIAL", 1)], CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.ExecSyncAsync(first, 1, CancellationToken.None));
        await db.ReleaseLockAsync(CancellationToken.None);
        var second = await db.BeginRunAsync(30, CancellationToken.None);
        try
        {
            await db.BulkInsertAsync(second, 1, [Product("NEW-COMPLETE", 2)], CancellationToken.None);
            await db.CompleteDownloadAsync(second, CancellationToken.None);
            await db.ExecSyncAsync(second, 1, CancellationToken.None);
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
        Assert.Equal(0, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products WHERE code='OLD-PARTIAL'"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products WHERE code='NEW-COMPLETE'"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_runs WHERE run_id='" + first + "' AND status='Failed' AND stage='Interrupted'"));
    }

    [PostgresFact]
    public async Task Duplicates_AreChosenByCategoryThenStableProductFields()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var uow = fixture.CreateUnitOfWork();
        var db = new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance, uow);
        var run = await db.BeginRunAsync(30, CancellationToken.None);
        try
        {
            var high = Product("DUPLICATE", 9);
            high.Prices.PrecioLista = "25";
            await db.BulkInsertAsync(run, 2, [high], CancellationToken.None);
            await db.BulkInsertAsync(run, 1, [Product("DUPLICATE", 10)], CancellationToken.None);
            Assert.Equal(10, await fixture.ScalarAsync("SELECT price FROM syscom_sync_staging WHERE code='DUPLICATE'"));
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
    }

    [PostgresFact]
    public async Task SuccessfulRuns_RecordMissingProductsWithoutDeletingThem()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var uow = fixture.CreateUnitOfWork();
        var db = new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance, uow);
        var first = await db.BeginRunAsync(30, CancellationToken.None);
        await db.BulkInsertAsync(first, 1, [Product("PRESENT", 1), Product("MISSING", 2)], CancellationToken.None);
        await db.CompleteDownloadAsync(first, CancellationToken.None);
        await db.ExecSyncAsync(first, 1, CancellationToken.None);
        await db.ReleaseLockAsync(CancellationToken.None);
        var second = await db.BeginRunAsync(30, CancellationToken.None);
        try
        {
            await db.BulkInsertAsync(second, 1, [Product("PRESENT", 1)], CancellationToken.None);
            await db.CompleteDownloadAsync(second, CancellationToken.None);
            await db.ExecSyncAsync(second, 1, CancellationToken.None);
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_missing WHERE code='MISSING'"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products WHERE code='MISSING'"));
    }

    [PostgresFact]
    public async Task Retention_PreservesLastSuccessfulBaseline()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var uow = fixture.CreateUnitOfWork();
        var db = new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance, uow);
        var first = await db.BeginRunAsync(1, CancellationToken.None);
        await db.BulkInsertAsync(first, 1, [Product("BASELINE", 1)], CancellationToken.None);
        await db.CompleteDownloadAsync(first, CancellationToken.None);
        await db.ExecSyncAsync(first, 1, CancellationToken.None);
        await db.ReleaseLockAsync(CancellationToken.None);
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                UPDATE syscom_sync_runs SET finished_at=now()-interval '2 days' WHERE run_id=@run
                """, connection);
            command.Parameters.AddWithValue("run", first);
            await command.ExecuteNonQueryAsync();
        }
        var second = await db.BeginRunAsync(1, CancellationToken.None);
        try
        {
            await db.CompleteDownloadAsync(second, CancellationToken.None);
            await db.ExecSyncAsync(second, 1, CancellationToken.None);
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_missing WHERE code='BASELINE'"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_runs WHERE run_id='" + first + "'"));
    }

    [PostgresFact]
    public async Task PriceInsertFailure_RollsBackDeletedPricesAndProductChanges()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                CREATE FUNCTION reject_price() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'test price insert rejection'; END; $$;
                CREATE TRIGGER reject_price_insert BEFORE INSERT ON portfolio_prices
                FOR EACH ROW EXECUTE FUNCTION reject_price();
                """, connection);
            await command.ExecuteNonQueryAsync();
        }
        await using var uow = fixture.CreateUnitOfWork();
        var db = new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance, uow);
        var run = await db.BeginRunAsync(30, CancellationToken.None);
        try
        {
            await db.BulkInsertAsync(run, 1, [Product("ROLLED-BACK", 1)], CancellationToken.None);
            await db.CompleteDownloadAsync(run, CancellationToken.None);
            await Assert.ThrowsAsync<PostgresException>(() => db.ExecSyncAsync(run, 1, CancellationToken.None));
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
        Assert.Equal(0, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products WHERE code='ROLLED-BACK'"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id=10 AND price=7"));
    }

    [PostgresFact]
    public async Task RepeatedPage_FailsRunWithoutPromotion()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var uow = fixture.CreateUnitOfWork();
        var client = new Mock<ISyscomClient>();
        client.Setup(c => c.GetCategoriesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            [new SyscomCategory { Id = 1, Nombre = "A" }]);
        client.Setup(c => c.GetProductsAsync(It.IsAny<SyscomProductsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyscomProductsRequest request, CancellationToken _) => new SyscomProductsSearchResponse
            {
                PaginaActual = 1, Paginas = 2, Products = [Product("PAGE-ONE", 1)]
            });
        var worker = new SyscomWorker(NullLoggerFactory.Instance, client.Object, uow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.RunAsync(CancellationToken.None));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_runs WHERE status='Failed' AND pages_loaded=1"));
        Assert.Equal(0, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products WHERE code='PAGE-ONE'"));
    }

    [PostgresTheory]
    [InlineData("empty-final", 1)]
    [InlineData("short-total", 2)]
    [InlineData("changed-total", 1)]
    public async Task IncompletePagination_FailsWithoutPromoting(string scenario, int expectedPages)
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var uow = fixture.CreateUnitOfWork();
        var client = new Mock<ISyscomClient>();
        client.Setup(c => c.GetCategoriesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            [new SyscomCategory { Id = 1, Nombre = "A" }]);
        client.Setup(c => c.GetProductsAsync(It.IsAny<SyscomProductsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyscomProductsRequest request, CancellationToken _) => request.Pagina == 1
                ? new SyscomProductsSearchResponse
                    { PaginaActual = 1, Paginas = 2, Total = scenario == "short-total" ? 3 : 2,
                      Products = [Product("PAGE-A", 1)] }
                : new SyscomProductsSearchResponse
                    { PaginaActual = 2, Paginas = 2, Total = scenario == "changed-total" ? 3
                        : scenario == "short-total" ? 3 : 2,
                      Products = scenario == "empty-final" ? [] : [Product("PAGE-B", 2)] });
        var worker = new SyscomWorker(NullLoggerFactory.Instance, client.Object, uow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.RunAsync(CancellationToken.None));
        Assert.Equal(1, await fixture.ScalarAsync($"SELECT count(*) FROM syscom_sync_runs WHERE status='Failed' AND pages_loaded={expectedPages}"));
        Assert.Equal(0, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products WHERE code IN ('PAGE-A','PAGE-B')"));
    }

    [PostgresFact]
    public async Task ExplicitEmptyCatalogue_CompletesWithoutStagedProducts()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var uow = fixture.CreateUnitOfWork();
        var client = new Mock<ISyscomClient>();
        client.Setup(c => c.GetCategoriesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            [new SyscomCategory { Id = 1, Nombre = "Empty" }]);
        client.Setup(c => c.GetProductsAsync(It.IsAny<SyscomProductsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyscomProductsSearchResponse
                { PaginaActual = 1, Paginas = 0, Total = 0, Products = [] });
        await new SyscomWorker(NullLoggerFactory.Instance, client.Object, uow).RunAsync(CancellationToken.None);
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_runs WHERE status='Succeeded' AND pages_loaded=1 AND products_staged=0"));
    }

    [PostgresFact]
    public async Task LargePage_StagesAcrossBoundedBatchesAndCountsOnce()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var uow = fixture.CreateUnitOfWork();
        var db = new SyscomDatabaseWorker(NullLogger<SyscomDatabaseWorker>.Instance, uow);
        var run = await db.BeginRunAsync(30, CancellationToken.None);
        try
        {
            var products = Enumerable.Range(1, 801).Select(i => Product($"BATCH-{i:D4}", i)).ToList();
            await db.BulkInsertAsync(run, 1, products, CancellationToken.None);
            await db.CompleteDownloadAsync(run, CancellationToken.None);
        }
        finally { await db.ReleaseLockAsync(CancellationToken.None); }
        Assert.Equal(801, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_staging"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_runs WHERE pages_loaded=1 AND products_seen=801 AND products_staged=801"));
    }

    [PostgresFact]
    public async Task CancelledBeforeCommit_RemainsRollbackableAndLeavesNoSentinel()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var uow = fixture.CreateUnitOfWork();
        await uow.BeginTransactionAsync(CancellationToken.None);
        await uow.ExecuteSqlRawAsync("""
            INSERT INTO portfolio_products(title,code,company_id,currency_id,price)
            VALUES('Sentinel','CANCELLED-COMMIT',1,1,1)
            """, ct: CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => uow.CommitTransactionAsync(cancelled.Token));
        await uow.RollbackTransactionAsync(CancellationToken.None);
        Assert.Equal(0, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products WHERE code='CANCELLED-COMMIT'"));
    }

    [PostgresFact]
    public async Task Cancellation_RecordsCancelledAndLeavesProductionUntouched()
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var uow = fixture.CreateUnitOfWork();
        using var cts = new CancellationTokenSource();
        var client = new Mock<ISyscomClient>();
        client.Setup(c => c.GetCategoriesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            [new SyscomCategory { Id = 1, Nombre = "A" }]);
        client.Setup(c => c.GetProductsAsync(It.IsAny<SyscomProductsRequest>(), It.IsAny<CancellationToken>()))
            .Returns((SyscomProductsRequest _, CancellationToken token) =>
            {
                cts.Cancel();
                return Task.FromCanceled<SyscomProductsSearchResponse>(token);
            });
        var worker = new SyscomWorker(NullLoggerFactory.Instance, client.Object, uow);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.RunAsync(cts.Token));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_runs WHERE status='Cancelled'"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id=10 AND price=7"));
    }

    public static SyscomProduct Product(string model, long id) => new()
    {
        Modelo = model, ProductoId = id, Titulo = model, SatKey = "46171604",
        SatDescription = "Security", Marca = "Brand",
        MeasurementUnit = new SyscomMeasurementUnit { ClaveUnidadSat = "H87", Nombre = "Piece" },
        Prices = new SyscomPrices { PrecioLista = "10" }
    };
}
