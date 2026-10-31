using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Syscom;
using Syscom.Load;
using Xunit.Abstractions;

namespace SyscomTestProject;

[Collection("Postgres sync")]
public class SyscomPerformanceSimulationTests(ITestOutputHelper output)
{
    [SimulationFact]
    public async Task PageSize_ReducesRequests_PreservesGlobalPrices_AndPersistsStageTimings()
    {
        var url = new Uri(Environment.GetEnvironmentVariable("SYSCOM_SIMULATION_URL")!);
        if (!url.IsLoopback || url.Scheme != "http") throw new InvalidOperationException("Local simulation required.");
        using var http = new HttpClient { BaseAddress = url };
        http.DefaultRequestHeaders.Add("x-mock-slug", "syscom");
        foreach (var size in new[] { 60, 500, 1000 })
        {
            using var reset = await http.PostAsync("/__control/reset?mode=paged", null);
            reset.EnsureSuccessStatusCode();
            await using var fixture = await PostgresSyncFixture.CreateAsync();
            await using var uow = fixture.CreateUnitOfWork();
            var client = new SyscomClient(http, new SyscomClientOptions("fixture-client", "fixture-secret"));
            var timer = Stopwatch.StartNew();
            await new SyscomWorker(NullLoggerFactory.Instance, client, uow,
                new SyscomSyncOptions { PageSize = size, BufferedPages = 2 }).RunAsync(CancellationToken.None);
            timer.Stop();
            var pages = (1200 + size - 1) / size;
            Assert.Equal(1, await fixture.ScalarAsync($"SELECT count(*) FROM syscom_sync_runs WHERE status='Succeeded' AND pages_loaded={pages} AND products_staged=1200"));
            Assert.Equal(1201, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products"));
            Assert.Equal(2402, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id IN (10,11)"));
            Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices p JOIN portfolio_products x ON x.id=p.portfolio_product_id WHERE x.company_id=2 AND p.portfolio_price_list_id=10 AND p.price=110"));
            Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id=12 AND price=9"));
            using var state = await http.GetFromJsonAsync<JsonDocument>("/__control/state");
            var productRequests = state!.RootElement.GetProperty("counts").EnumerateObject()
                .Where(p => p.Name.StartsWith("GET /api/v1/productos?")).ToList();
            Assert.Equal(pages, productRequests.Sum(p => p.Value.GetInt32()));
            Assert.All(productRequests, request => Assert.Contains($"limit={size}", request.Name));
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT metrics::text FROM syscom_sync_runs ORDER BY started_at DESC LIMIT 1", connection);
            var json = (string)(await command.ExecuteScalarAsync())!;
            using var metrics = JsonDocument.Parse(json);
            foreach (var key in new[] { "total_elapsed_ms", "preparation_ms", "download_ms", "staging_ms", "catalog_promotion_ms", "price_recalculation_ms", "promotion_ms" })
                Assert.True(metrics.RootElement.GetProperty(key).GetInt64() >= 0, key);
            Assert.True(metrics.RootElement.GetProperty("download_ms").GetInt64() > 0);
            Assert.True(metrics.RootElement.GetProperty("staging_ms").GetInt64() > 0);
            output.WriteLine($"page_size={size}; requests={pages}; elapsed_ms={timer.ElapsedMilliseconds}; metrics={json}");
        }
    }
}
