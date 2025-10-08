using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using IDOneRepository;
using IDOneRepository.Data.Entities;
using Microsoft.Extensions.Logging;
using Npgsql;
using Syscom.Models;

namespace Syscom.Load;

public partial class SyscomDatabaseWorker(ILogger<SyscomDatabaseWorker> logger, IUnitOfWork uow)
{
    private readonly string _tableName = BuildStagingTableName();

    public async Task CreateStagingTableAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("  > Creating staging table {TableName}", _tableName);
        var tableTrim = _tableName.Trim('"');
        var sql = $"""
                   CREATE TABLE IF NOT EXISTS {_tableName} (
                       code          varchar(35) NOT NULL,
                       name          varchar(350),
                       details       text,
                       sat_unit      varchar(14),
                       sat_unit_desc varchar(350),
                       sat_key       varchar(14),
                       sat_key_desc  varchar(350),
                       price         numeric(14,4),
                       currency      varchar(3),
                       trade_mark    varchar(70),
                       PRIMARY KEY (code)
                   );
                   CREATE INDEX IF NOT EXISTS ix_{tableTrim}_sat ON {_tableName} (code, sat_key, sat_unit);
                   """;

        await uow.ExecuteSqlRawAsync(sql, ct: cancellationToken);
        logger.LogInformation("  > Finished creating staging table {TableName}", _tableName);
    }

    public async Task BulkInsertAsync(List<SyscomProduct> products, CancellationToken cancellationToken)
    {
        // Prepare entities for Upsert
        var entities = new List<SyscomProducts>();
        foreach (var p in products)
        {
            var code = NormalizeCode(p.Modelo);
            if (string.IsNullOrEmpty(code)) continue;

            var name = Truncate(FirstNonEmpty(p.Titulo), 350);
            var details = p.Titulo;
            var (satUnit, satUnitDescription) = GetSatUnitKey(p);
            var (satKey, satKeyDescription) = GetSatProductKey(p);
            var (price, currency) = ExtractPriceAndCurrency(p);

            if (string.IsNullOrEmpty(code)
                || string.IsNullOrEmpty(name)
                || string.IsNullOrEmpty(satUnit)
                || string.IsNullOrEmpty(satKey)
                || string.IsNullOrEmpty(currency)
                || price == null)
            {
                logger.LogWarning("  !! Skipping product {Code} due to missing data", code);
                continue;
            }

            var mark = Truncate(p.Marca, 70);
            entities.Add(new SyscomProducts
            {
                Code = code,
                Name = name,
                Details = details,
                SatUnit = satUnit,
                SatUnitDesc = satUnitDescription,
                SatKey = satKey,
                SatKeyDesc = satKeyDescription,
                Price = price,
                Currency = currency,
                TradeMark = mark
            });
        }

        logger.LogInformation("  > Upserting {Count} products into staging table {TableName}", entities.Count,
            _tableName);

        var upserted = await uow.SyscomProducts.UpsertRangeAsync(
            entities, e => e.Code, e => e.Code,
            (db, ins) => new SyscomProducts
            {
                Name = ins.Name,
                Details = ins.Details,
                SatUnit = ins.SatUnit,
                SatUnitDesc = ins.SatUnitDesc,
                SatKey = ins.SatKey,
                SatKeyDesc = ins.SatKeyDesc,
                Price = ins.Price,
                Currency = ins.Currency,
                TradeMark = ins.TradeMark
            }, cancellationToken
        );
        logger.LogInformation("  > Finished upserting {Count} products into staging table {TableName}",
            upserted, _tableName);
    }

    public async Task ExecSyncAsync(long companyId, CancellationToken cancellationToken = default)
    {
        var sql = BuildImportSql(_tableName);
        var param = new NpgsqlParameter("companyId", companyId);
        await uow.ExecuteSqlRawAsync(sql, [param], cancellationToken);
    }

    #region Data processing API

    private static string NormalizeCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim();
        var slice = trimmed.Length <= 35 ? trimmed : trimmed[..35];
        return slice.ToUpperInvariant();
    }

    private static string? Truncate(string? v, int max)
        => string.IsNullOrEmpty(v) ? v : (v.Length <= max ? v : v[..max]);

    private static string? FirstNonEmpty(params string?[] parts)
        => parts.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

    private static (decimal? price, string? currency) ExtractPriceAndCurrency(SyscomProduct p)
    {
        var raw = p.Prices?.PrecioLista ?? p.Prices?.Precio1 ??
            p.Prices?.PrecioEspecial ?? p.Prices?.PrecioDescuento;
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);

        var currency = raw.Contains("USD", StringComparison.OrdinalIgnoreCase) ? "USD" :
            raw.Contains("EUR", StringComparison.OrdinalIgnoreCase) ? "EUR" : "MXN";

        var cleaned = new string(raw.Where(ch => char.IsDigit(ch) || ch == '.' || ch == '-').ToArray());
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var val)
            ? (Math.Round(val, 4), currency)
            : (null, currency);
    }

    private static (string satUnit, string satUnitDescription) GetSatUnitKey(SyscomProduct p)
        => (p.MeasurementUnit.ClaveUnidadSat, Truncate(p.MeasurementUnit.Nombre, 350) ?? string.Empty);

    private static (string satKey, string satKeyDescription) GetSatProductKey(SyscomProduct p)
    {
        var regex = SayKeyRegex();
        // 46171604 Sistemas de alarma, Sistemas de seguridad

        return (
            regex.IsMatch(p.SatKey) ? p.SatKey : "46171604",
            Truncate(p.SatDescription, 350) ?? string.Empty
        );
    }

    #endregion

    #region Query API

    private static string BuildStagingTableName()
    {
        // Fixed table name to allow EF mapping and Upsert operations
        return "\"syscom_products_staging\"";
    }

    private static string BuildImportSql(string tableName)
    {
        var sb = new StringBuilder();
        sb.Append($"""

                   -- A) SAT products
                       -- 1️⃣ Update existing SAT product titles where code matches the sat_key in staging
                       UPDATE catalogs_sat_products AS sat
                       SET title = LEFT(COALESCE(NULLIF(BTRIM(s.sat_key_desc), ''), s.sat_key), 350),
                           updated_at = NOW()
                       FROM {tableName} AS s
                       WHERE sat.code = s.sat_key
                         AND s.sat_key IS NOT NULL
                         AND BTRIM(s.sat_key) <> '';
                      
                       -- 2️⃣ Insert SAT keys that don't exist yet
                       INSERT INTO catalogs_sat_products (code, title, created_at, updated_at)
                       SELECT DISTINCT s.sat_key,
                              LEFT(COALESCE(NULLIF(BTRIM(s.sat_key_desc), ''), s.sat_key), 350),
                              NOW(), NOW()
                       FROM {tableName} s
                       WHERE s.sat_key IS NOT NULL
                         AND BTRIM(s.sat_key) <> ''
                         AND NOT EXISTS (
                             SELECT 1 FROM catalogs_sat_products csp
                             WHERE csp.code = s.sat_key
                         );   

                   -- B) Measurements (store_type=0)
                       -- 1️⃣ Update existing measurement titles/descriptions for matching sat_key + store_type = 0
                       UPDATE catalogs_measurements AS cm
                       SET
                           title = LEFT(COALESCE(NULLIF(BTRIM(s.sat_unit_desc), ''), s.sat_unit), 70),
                           sat_description = LEFT(COALESCE(NULLIF(BTRIM(s.sat_unit_desc), ''), s.sat_unit), 350),
                           updated_at = NOW()
                       FROM {tableName} AS s
                       WHERE cm.sat_key = s.sat_unit
                         AND cm.store_type = 0
                         AND s.sat_unit IS NOT NULL
                         AND BTRIM(s.sat_unit) <> '';
                         
                       -- 2️⃣ Insert new measurement sat_units not yet in catalogs_measurements (store_type = 0)
                       INSERT INTO catalogs_measurements (
                           title, sat_description, sat_key, store_type, created_at, updated_at
                       )
                       SELECT DISTINCT
                           LEFT(COALESCE(NULLIF(BTRIM(s.sat_unit_desc), ''), s.sat_unit), 70),
                           LEFT(COALESCE(NULLIF(BTRIM(s.sat_unit_desc), ''), s.sat_unit), 350),
                           s.sat_unit,
                           0,
                           NOW(),
                           NOW()
                       FROM {tableName} s
                       WHERE s.sat_unit IS NOT NULL
                         AND BTRIM(s.sat_unit) <> ''
                         AND NOT EXISTS (
                             SELECT 1 FROM catalogs_measurements cm
                             WHERE cm.sat_key = s.sat_unit
                               AND cm.store_type = 0
                         );

                   -- C) portfolio_products
                   WITH src AS (
                     SELECT DISTINCT ON (s.code)
                            LEFT(COALESCE(NULLIF(BTRIM(s.name), ''), s.code), 350)  AS title,
                            s.code                                                 AS code,
                            cm.id                                                  AS catalogs_measurement_id,
                            ''                                                     AS details,
                            0                                                      AS parent_id,
                            NOW()                                                  AS created_at,
                            NOW()                                                  AS updated_at,
                            @companyId::bigint                                     AS company_id,
                            CASE UPPER(BTRIM(s.currency))
                                 WHEN 'USD' THEN 2 WHEN 'EUR' THEN 3 ELSE 1 END    AS currency_id,
                            ROUND((s.price)::numeric, 4)                           AS price,
                            LEFT(NULLIF(BTRIM(s.trade_mark), ''), 70)              AS trade_mark,
                            sat.id                                                 AS sat_id
                     FROM {tableName} s
                          LEFT JOIN catalogs_sat_products sat
                                 ON sat.code = s.sat_key
                          LEFT JOIN catalogs_measurements cm
                                 ON cm.sat_key = s.sat_unit AND cm.store_type = 0
                     WHERE s.code IS NOT NULL AND BTRIM(s.code) <> ''
                     ORDER BY s.code, s.price DESC NULLS LAST
                   )
                   INSERT INTO portfolio_products (
                       title, code, catalogs_measurement_id, details, parent_id,
                       created_at, updated_at, company_id, currency_id, price,
                       trade_mark, catalogs_sat_product_id
                   )
                   SELECT title, code, catalogs_measurement_id, details, parent_id,
                          created_at, updated_at, company_id, currency_id, price,
                          trade_mark, sat_id
                   FROM src
                   ON CONFLICT (company_id, code) DO UPDATE
                   SET title                    = EXCLUDED.title,
                       trade_mark               = EXCLUDED.trade_mark,
                       price                    = EXCLUDED.price,
                       currency_id              = EXCLUDED.currency_id,
                       catalogs_measurement_id  = EXCLUDED.catalogs_measurement_id,
                       catalogs_sat_product_id  = EXCLUDED.catalogs_sat_product_id,
                       updated_at               = NOW();

                   """);
        return sb.ToString();
    }

    [GeneratedRegex(@"^\d{8}$")]
    private static partial Regex SayKeyRegex();

    #endregion
}