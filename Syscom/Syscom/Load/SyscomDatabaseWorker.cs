using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using IDOneRepository;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Syscom.Models;

namespace Syscom.Load;

public sealed class SyscomSyncAlreadyRunningException() : InvalidOperationException("A SYSCOM synchronization is already running.");

public sealed partial class SyscomDatabaseWorker(ILogger<SyscomDatabaseWorker> logger, IUnitOfWork uow,
    SyscomSyncMetrics? metrics = null)
{
    private const long AdvisoryLockKey = 0x535953434F4D; // SYSCOM, shared by all workers
    private bool _ownsLock;
    private DbTransaction? _transaction;

    public Task<Guid> BeginRunAsync(int retentionDays, CancellationToken ct) => BeginRunAsync(retentionDays, 1, ct);

    public async Task<Guid> BeginRunAsync(int retentionDays, long companyId, CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);
        _ownsLock = (bool)(await ScalarAsync("SELECT pg_try_advisory_lock(@key)", ct,
            ("key", AdvisoryLockKey)) ?? false);
        if (!_ownsLock) throw new SyscomSyncAlreadyRunningException();
        if (!(bool)(await ScalarAsync("""
                SELECT atttypid IN ('text'::regtype,'varchar'::regtype) AND atttypmod=-1
                FROM pg_attribute WHERE attrelid='portfolio_products'::regclass AND attname='code'
                """, ct) ?? false))
            throw SyscomFailureDiagnostics.Validation(SyscomFailureReason.CodeSchemaMigrationRequired);
        await ExecuteAsync(SchemaSql, ct);
        await ExecuteAsync("""
            UPDATE syscom_sync_runs SET status='Failed', stage='Interrupted', finished_at=now()
            WHERE status='Running'
            """, ct);
        var runId = Guid.NewGuid();
        await ExecuteAsync("INSERT INTO syscom_sync_runs (run_id, company_id, status, stage) VALUES (@run, @company, 'Running', 'Downloading')",
            ct, ("run", runId), ("company", companyId));
        await ExecuteAsync("""
            DELETE FROM syscom_sync_runs
            WHERE finished_at < now() - make_interval(days => @days)
              AND status IN ('Succeeded','Failed','Cancelled')
              AND run_id NOT IN (SELECT DISTINCT ON (company_id) run_id FROM syscom_sync_runs
                  WHERE status='Succeeded' ORDER BY company_id, finished_at DESC, run_id DESC)
            """, ct, ("days", retentionDays));
        return runId;
    }

    private sealed record StagedProduct(string Code, string RawModel, string WinnerKey,
        long CategoryId, long ProductId, string Name, string? Details, string SatUnit,
        string? SatUnitDesc, string SatKey, string? SatKeyDesc, decimal Price,
        string Currency, string? TradeMark);

    private static readonly JsonSerializerOptions StageJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task BulkInsertAsync(Guid runId, long categoryId, List<SyscomProduct> products, CancellationToken ct)
    {
        using var stagingMeasurement = metrics?.Measure(SyscomSyncStage.Staging);
        // A page is the memory boundary. Resolve duplicates before sending bounded JSON batches.
        var unique = new Dictionary<string, StagedProduct>(StringComparer.Ordinal);
        var skipped = 0;
        foreach (var product in products)
        {
            ct.ThrowIfCancellationRequested();
            var code = NormalizeCode(product.Modelo);
            var rawModel = product.Modelo?.Trim().ToUpperInvariant() ?? string.Empty;
            var name = Truncate(product.Titulo, 350);
            var satUnit = Truncate(product.MeasurementUnit?.ClaveUnidadSat, 14);
            var satKey = Regex.IsMatch(product.SatKey ?? "", "^[0-9]{8}$") ? product.SatKey! : "46171604";
            var (price, currency) = ExtractPriceAndCurrency(product);
            if (code.Length == 0 || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(satUnit) ||
                string.IsNullOrWhiteSpace(currency) || price is null or <= 0)
            {
                skipped++;
                continue;
            }
            var unitDescription = Truncate(product.MeasurementUnit?.Nombre, 350);
            var satDescription = Truncate(product.SatDescription, 350);
            var mark = Truncate(product.Marca, 70);
            var winnerKey = JsonSerializer.Serialize(new[]
            {
                categoryId.ToString("D20"), product.ProductoId.ToString("D20"), name, product.Titulo ?? "",
                satUnit, unitDescription ?? "", satKey, satDescription ?? "",
                price.Value.ToString("000000000000000.0000", CultureInfo.InvariantCulture), currency, mark ?? ""
            });
            var row = new StagedProduct(code, rawModel, winnerKey, categoryId, product.ProductoId,
                name, product.Titulo, satUnit, unitDescription, satKey, satDescription, price.Value,
                currency, mark);
            if (unique.TryGetValue(code, out var prior))
            {
                if (string.CompareOrdinal(winnerKey, prior.WinnerKey) < 0) unique[code] = row;
            }
            else unique.Add(code, row);
        }

        var transaction = await uow.BeginTransactionAsync(ct);
        _transaction = transaction.GetDbTransaction();
        try
        {
            var rows = unique.Values.OrderBy(row => row.Code, StringComparer.Ordinal).ToArray();
            foreach (var batch in rows.Chunk(500))
            {
                ct.ThrowIfCancellationRequested();
                var json = JsonSerializer.Serialize(batch, StageJsonOptions);
                await ExecuteAsync(StageBatchSql, ct, ("run", runId), ("rows", json));
            }
            await ExecuteAsync("""
                UPDATE syscom_sync_runs SET pages_loaded=pages_loaded+1,
                    products_seen=products_seen+@seen, products_skipped=products_skipped+@skipped
                WHERE run_id=@run AND status='Running' AND stage='Downloading'
                """, ct, ("run", runId), ("seen", products.Count), ("skipped", skipped));
            await uow.CommitTransactionAsync(ct);
            logger.LogInformation("SYSCOM run {RunId} staged {Count} distinct valid rows from one page; {Skipped} skipped.",
                runId, unique.Count, skipped);
        }
        catch
        {
            await uow.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
        finally { _transaction = null; }
    }

    public Task CompleteDownloadAsync(Guid runId, CancellationToken ct) => ExecuteAsync("""
        UPDATE syscom_sync_runs SET stage='Downloaded',
            products_staged=(SELECT count(*) FROM syscom_sync_staging WHERE run_id=@run)
        WHERE run_id=@run AND status='Running' AND stage='Downloading'
        """, ct, ("run", runId));

    private const string StageBatchSql = """
        INSERT INTO syscom_sync_staging
        (run_id, code, raw_model, winner_key, category_id, product_id, name, details, sat_unit,
         sat_unit_desc, sat_key, sat_key_desc, price, currency, trade_mark)
        SELECT @run, incoming.code, incoming.raw_model, incoming.winner_key,
            incoming.category_id, incoming.product_id, incoming.name, incoming.details,
            incoming.sat_unit, incoming.sat_unit_desc, incoming.sat_key, incoming.sat_key_desc,
            incoming.price, incoming.currency, incoming.trade_mark
        FROM jsonb_to_recordset(@rows::jsonb) AS incoming(
            code text, raw_model text, winner_key text, category_id bigint, product_id bigint,
            name text, details text, sat_unit text, sat_unit_desc text, sat_key text,
            sat_key_desc text, price numeric, currency text, trade_mark text)
        ON CONFLICT (run_id, code) DO UPDATE SET
            winner_key=EXCLUDED.winner_key, category_id=EXCLUDED.category_id,
            product_id=EXCLUDED.product_id, name=EXCLUDED.name, details=EXCLUDED.details,
            sat_unit=EXCLUDED.sat_unit, sat_unit_desc=EXCLUDED.sat_unit_desc,
            sat_key=EXCLUDED.sat_key, sat_key_desc=EXCLUDED.sat_key_desc,
            price=EXCLUDED.price, currency=EXCLUDED.currency, trade_mark=EXCLUDED.trade_mark
        WHERE EXCLUDED.winner_key COLLATE "C" < syscom_sync_staging.winner_key COLLATE "C"
        """;

    public async Task ExecSyncAsync(Guid runId, long companyId, CancellationToken ct)
    {
        using var promotionMeasurement = metrics?.Measure(SyscomSyncStage.Promotion);
        if ((string?)await ScalarAsync("""
                SELECT stage FROM syscom_sync_runs
                WHERE run_id=@run AND company_id=@company AND status='Running'
                """, ct, ("run", runId), ("company", companyId)) != "Downloaded")
            throw SyscomFailureDiagnostics.Validation(SyscomFailureReason.DownloadIncomplete);
        await ExecuteAsync("UPDATE syscom_sync_runs SET stage='Promoting' WHERE run_id=@run", ct, ("run", runId));
        var transaction = await uow.BeginTransactionAsync(ct);
        _transaction = transaction.GetDbTransaction();
        try
        {
            using (metrics?.Measure(SyscomSyncStage.CatalogPromotion))
            {
                await ReconcileLegacyCodesAsync(runId, companyId, ct);
                await ExecuteAsync(PromotionSql, ct, ("run", runId), ("company", companyId));
            }
            using (metrics?.Measure(SyscomSyncStage.PriceRecalculation))
            {
                var duplicateRates = Convert.ToInt64(await ScalarAsync("""
                SELECT count(*) FROM (SELECT currency_id FROM exchange_rates WHERE principal=true
                GROUP BY currency_id HAVING count(*) > 1) duplicates
                """, ct));
                if (duplicateRates > 0)
                    throw SyscomFailureDiagnostics.Validation(SyscomFailureReason.DuplicateExchangeRates, actualCount: duplicateRates);
                await ExecuteAsync(CandidateSql, ct);
                var invalid = Convert.ToInt64(await ScalarAsync("SELECT count(*) FROM syscom_sync_price_candidates WHERE price IS NULL OR price < 0", ct));
                var expected = Convert.ToInt64(await ScalarAsync("""
                SELECT count(*) FROM portfolio_price_lists l CROSS JOIN portfolio_products p
                WHERE l.percent_reference IS NOT NULL AND p.price > 0
                """, ct));
                var actual = Convert.ToInt64(await ScalarAsync("SELECT count(*) FROM syscom_sync_price_candidates", ct));
                if (invalid != 0 || actual != expected)
                    throw SyscomFailureDiagnostics.Validation(SyscomFailureReason.InvalidPriceCandidates, expectedCount: expected, actualCount: actual, invalidCount: invalid);
                await ExecuteAsync("""
                DELETE FROM portfolio_prices WHERE portfolio_price_list_id IN
                    (SELECT id FROM portfolio_price_lists WHERE percent_reference IS NOT NULL)
                """, ct);
                await ExecuteAsync("""
                INSERT INTO portfolio_prices
                    (portfolio_price_list_id, portfolio_product_id, price, created_at, updated_at, auto_update)
                SELECT portfolio_price_list_id, portfolio_product_id, price, now(), now(), false
                FROM syscom_sync_price_candidates
                """, ct);
            }
            await ExecuteAsync("""
                INSERT INTO syscom_sync_missing (run_id, code)
                SELECT @run, old.code FROM syscom_sync_staging old
                WHERE old.run_id=(SELECT run_id FROM syscom_sync_runs
                    WHERE status='Succeeded' AND company_id=@company ORDER BY finished_at DESC LIMIT 1)
                  AND NOT EXISTS (SELECT 1 FROM syscom_sync_staging current_run
                    WHERE current_run.run_id=@run AND current_run.raw_model=old.raw_model)
                ON CONFLICT DO NOTHING
                """, ct, ("run", runId), ("company", companyId));
            await ExecuteAsync("""
                UPDATE syscom_sync_runs SET prices_replaced=(SELECT count(*) FROM syscom_sync_price_candidates),
                    products_missing=(SELECT count(*) FROM syscom_sync_missing WHERE run_id=@run),
                    status='Succeeded', stage='Complete', finished_at=now()
                WHERE run_id=@run
                """, ct, ("run", runId));
            await uow.CommitTransactionAsync(ct);
        }
        catch
        {
            await uow.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
        finally { _transaction = null; }
    }

    public Task FailRunAsync(Guid runId, string status, CancellationToken ct) => ExecuteAsync("""
        UPDATE syscom_sync_runs SET status=@status, finished_at=now() WHERE run_id=@run AND status='Running'
        """, ct, ("run", runId), ("status", status));

    public Task RecordMetricsAsync(Guid runId, CancellationToken ct)
    {
        if (metrics is null) return Task.CompletedTask;
        if (_transaction is not null)
            throw new InvalidOperationException("SYSCOM metrics must be recorded outside a promotion or staging transaction.");
        return ExecuteAsync("UPDATE syscom_sync_runs SET metrics=@metrics::jsonb WHERE run_id=@run",
            ct, ("metrics", metrics.ToJson()), ("run", runId));
    }

    public async Task ReleaseLockAsync(CancellationToken ct)
    {
        if (!_ownsLock) return;
        try { await ScalarAsync("SELECT pg_advisory_unlock(@key)", ct, ("key", AdvisoryLockKey)); }
        finally { _ownsLock = false; }
    }

    private async Task<int> ExecuteAsync(string sql, CancellationToken ct, params (string, object?)[] args)
    {
        await using var cmd = Command(sql, args);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<object?> ScalarAsync(string sql, CancellationToken ct, params (string, object?)[] args)
    {
        await using var cmd = Command(sql, args);
        return await cmd.ExecuteScalarAsync(ct);
    }

    private DbCommand Command(string sql, (string, object?)[] args)
    {
        var cmd = uow.GetConnection().CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = _transaction;
        foreach (var (name, value) in args)
        {
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(parameter);
        }
        return cmd;
    }

    internal static string NormalizeCode(string? raw) => raw?.Trim().ToUpperInvariant() ?? "";

    private static string? Truncate(string? value, int length) =>
        value is { Length: var size } && size > length ? value[..length] : value;
    private static (decimal?, string?) ExtractPriceAndCurrency(SyscomProduct p)
    {
        var raw = p.Prices?.PrecioDescuento ?? p.Prices?.PrecioLista ?? p.Prices?.Precio1 ?? p.Prices?.PrecioEspecial;
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        var cleaned = new string(raw.Where(c => char.IsDigit(c) || c is '.' or '-').ToArray());
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
            ? (Math.Round(price, 4), "USD") : (null, "USD");
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS syscom_sync_runs (
            run_id uuid PRIMARY KEY, company_id bigint NOT NULL DEFAULT 1,
            started_at timestamptz NOT NULL DEFAULT now(), finished_at timestamptz,
            status text NOT NULL, stage text NOT NULL, pages_loaded bigint NOT NULL DEFAULT 0,
            products_seen bigint NOT NULL DEFAULT 0, products_skipped bigint NOT NULL DEFAULT 0,
            products_staged bigint NOT NULL DEFAULT 0, products_missing bigint NOT NULL DEFAULT 0,
            prices_replaced bigint NOT NULL DEFAULT 0, metrics jsonb);
        ALTER TABLE syscom_sync_runs ADD COLUMN IF NOT EXISTS company_id bigint NOT NULL DEFAULT 1;
        ALTER TABLE syscom_sync_runs ADD COLUMN IF NOT EXISTS metrics jsonb;
        CREATE TABLE IF NOT EXISTS syscom_sync_staging (
            run_id uuid NOT NULL REFERENCES syscom_sync_runs(run_id) ON DELETE CASCADE,
            code text NOT NULL, raw_model text NOT NULL, winner_key text NOT NULL,
            category_id bigint NOT NULL,
            product_id bigint NOT NULL, name varchar(350) NOT NULL, details text,
            sat_unit varchar(14) NOT NULL, sat_unit_desc varchar(350), sat_key varchar(14) NOT NULL,
            sat_key_desc varchar(350), price numeric(14,4) NOT NULL, currency varchar(3) NOT NULL,
            trade_mark varchar(70), PRIMARY KEY(run_id, code));
        CREATE INDEX IF NOT EXISTS ix_syscom_sync_staging_sat ON syscom_sync_staging(run_id, sat_key, sat_unit);
        CREATE TABLE IF NOT EXISTS syscom_sync_missing (
            run_id uuid NOT NULL REFERENCES syscom_sync_runs(run_id) ON DELETE CASCADE,
            code text NOT NULL, PRIMARY KEY(run_id, code));
        DO $$ BEGIN
            IF EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid='syscom_sync_staging'::regclass
                       AND attname='code' AND atttypmod<>-1) THEN
                ALTER TABLE syscom_sync_staging ALTER COLUMN code TYPE text;
            END IF;
            IF EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid='syscom_sync_missing'::regclass
                       AND attname='code' AND atttypmod<>-1) THEN
                ALTER TABLE syscom_sync_missing ALTER COLUMN code TYPE text;
            END IF;
        END $$;
        """;

    private async Task ReconcileLegacyCodesAsync(Guid runId, long companyId, CancellationToken ct)
    {
        // Lock existing identities before selecting a legacy owner; never delete/reinsert
        // products because prices, inventory and commercial documents reference their IDs.
        await ExecuteAsync("SELECT id FROM portfolio_products WHERE company_id=@company FOR UPDATE", ct, ("company", companyId));
        await ExecuteAsync("""
            CREATE TEMP TABLE syscom_code_candidates ON COMMIT DROP AS
            SELECT candidates.*, count(*) OVER (PARTITION BY id)=1 OR title_matches AS identity_matches
            FROM (
            SELECT p.id, s.code, p.title=s.name AS title_matches
            FROM portfolio_products p JOIN syscom_sync_staging s ON s.run_id=@run
              AND length(s.code)>35 AND p.code<>s.code
              AND (p.code=left(s.code,35) OR p.code='SYSCOM-' || s.product_id::text)
            WHERE p.company_id=@company
              AND NOT EXISTS (SELECT 1 FROM syscom_sync_staging exact
                              WHERE exact.run_id=@run AND exact.code=p.code)
            ) candidates;
            """, ct, ("run", runId), ("company", companyId));
        var ambiguous = Convert.ToInt64(await ScalarAsync("""
            SELECT count(*) FROM (
              SELECT id FROM syscom_code_candidates GROUP BY id
              HAVING count(*) FILTER (WHERE identity_matches)<>1
              UNION
              SELECT c.id FROM syscom_code_candidates c
              WHERE c.identity_matches AND (
                EXISTS (SELECT 1 FROM syscom_code_candidates other
                        WHERE other.identity_matches AND other.code=c.code AND other.id<>c.id)
                OR EXISTS (SELECT 1 FROM portfolio_products destination
                           WHERE destination.company_id=@company AND destination.code=c.code AND destination.id<>c.id))
            ) unresolved
            """, ct, ("company", companyId)));
        if (ambiguous != 0)
            throw SyscomFailureDiagnostics.Validation(SyscomFailureReason.LegacyCodeIdentityAmbiguous, actualCount: ambiguous);
        await ExecuteAsync("""
            UPDATE portfolio_products p SET code=c.code, updated_at=now()
            FROM syscom_code_candidates c WHERE p.id=c.id AND c.identity_matches
            """, ct);
    }

    private const string PromotionSql = """
        INSERT INTO catalogs_sat_products (code, title, created_at, updated_at)
        SELECT DISTINCT ON (s.sat_key) s.sat_key,
            LEFT(COALESCE(NULLIF(BTRIM(s.sat_key_desc), ''), s.sat_key), 350), now(), now()
        FROM syscom_sync_staging s WHERE s.run_id=@run
        ORDER BY s.sat_key, s.code
        ON CONFLICT (code) DO UPDATE SET
            title=CASE WHEN NULLIF(BTRIM(catalogs_sat_products.title),'') IS NULL
                THEN EXCLUDED.title ELSE catalogs_sat_products.title END, updated_at=now();

        UPDATE catalogs_measurements cm SET title=LEFT(COALESCE(NULLIF(BTRIM(s.sat_unit_desc),''),s.sat_unit),70),
            sat_description=LEFT(COALESCE(NULLIF(BTRIM(s.sat_unit_desc),''),s.sat_unit),350), updated_at=now()
        FROM (SELECT DISTINCT ON (sat_unit) sat_unit, sat_unit_desc FROM syscom_sync_staging
            WHERE run_id=@run ORDER BY sat_unit, code) s
        WHERE cm.sat_key=s.sat_unit AND cm.store_type=0;
        INSERT INTO catalogs_measurements
            (title, sat_description, sat_key, store_type, created_at, updated_at)
        SELECT LEFT(COALESCE(NULLIF(BTRIM(s.sat_unit_desc),''),s.sat_unit),70),
            LEFT(COALESCE(NULLIF(BTRIM(s.sat_unit_desc),''),s.sat_unit),350), s.sat_unit, 0, now(), now()
        FROM (SELECT DISTINCT ON (sat_unit) sat_unit, sat_unit_desc FROM syscom_sync_staging
            WHERE run_id=@run ORDER BY sat_unit, code) s
        WHERE NOT EXISTS (SELECT 1 FROM catalogs_measurements cm
            WHERE cm.sat_key=s.sat_unit AND cm.store_type=0);

        INSERT INTO portfolio_products
            (title, code, catalogs_measurement_id, details, parent_id, created_at, updated_at,
             company_id, currency_id, price, trade_mark, catalogs_sat_product_id)
        SELECT s.name, s.code, cm.id, '', 0, now(), now(), @company,
            CASE upper(s.currency) WHEN 'USD' THEN 2 WHEN 'EUR' THEN 3 ELSE 1 END,
            s.price, s.trade_mark, sat.id
        FROM syscom_sync_staging s
        JOIN catalogs_sat_products sat ON sat.code=s.sat_key
        LEFT JOIN portfolio_products existing ON existing.company_id=@company AND existing.code=s.code
        JOIN LATERAL (
            SELECT measurement.id FROM catalogs_measurements measurement
            WHERE measurement.sat_key=s.sat_unit AND measurement.store_type=0
            ORDER BY CASE WHEN measurement.id=existing.catalogs_measurement_id THEN 0 ELSE 1 END,
                measurement.id
            LIMIT 1
        ) cm ON true
        WHERE s.run_id=@run
        ON CONFLICT (company_id, code) DO UPDATE SET
            title=EXCLUDED.title, trade_mark=EXCLUDED.trade_mark, price=EXCLUDED.price,
            currency_id=EXCLUDED.currency_id, catalogs_measurement_id=EXCLUDED.catalogs_measurement_id,
            catalogs_sat_product_id=EXCLUDED.catalogs_sat_product_id, updated_at=now();
        """;

    private const string CandidateSql = """
        CREATE TEMP TABLE syscom_sync_price_candidates ON COMMIT DROP AS
        SELECT l.id AS portfolio_price_list_id, p.id AS portfolio_product_id,
            ROUND((CASE
                WHEN p.currency_id=l.currency_id THEN p.price
                WHEN pc.principal AND lc.id IS NOT NULL AND NOT COALESCE(lc.principal,false) AND lr.exchange_rate > 0
                    THEN p.price / lr.exchange_rate
                WHEN lc.principal AND pc.id IS NOT NULL AND NOT COALESCE(pc.principal,false) AND pr.exchange_rate > 0
                    THEN p.price * pr.exchange_rate
                WHEN pc.id IS NOT NULL AND lc.id IS NOT NULL
                    AND NOT COALESCE(pc.principal,false) AND NOT COALESCE(lc.principal,false)
                    AND pr.exchange_rate > 0 AND lr.exchange_rate > 0
                    THEN p.price * pr.exchange_rate / lr.exchange_rate
                ELSE NULL END)
                / CASE WHEN COALESCE(p.pr_quantity,0)=0 THEN 1 ELSE p.pr_quantity END
                * (1 + l.percent_reference / 100), 4) AS price
        FROM portfolio_price_lists l CROSS JOIN portfolio_products p
        LEFT JOIN currencies pc ON pc.id=p.currency_id
        LEFT JOIN currencies lc ON lc.id=l.currency_id
        LEFT JOIN LATERAL (SELECT exchange_rate FROM exchange_rates
            WHERE currency_id=p.currency_id AND principal=true ORDER BY id DESC LIMIT 1) pr ON true
        LEFT JOIN LATERAL (SELECT exchange_rate FROM exchange_rates
            WHERE currency_id=l.currency_id AND principal=true ORDER BY id DESC LIMIT 1) lr ON true
        WHERE l.percent_reference IS NOT NULL AND p.price > 0;
        """;
}
