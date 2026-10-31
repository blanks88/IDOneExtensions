using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Syscom;
using Syscom.Load;

namespace SyscomTestProject;

public sealed class SimulationFactAttribute : FactAttribute
{
    public SimulationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SYSCOM_SIMULATION_URL")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SYSCOM_TEST_DB_CONNECTION")))
            Skip = "Requires local Postman simulation and disposable PostgreSQL.";
    }
}

[Collection("Postgres sync")]
public class SyscomSimulationTests
{
    private static HttpClient Http()
    {
        var url = new Uri(Environment.GetEnvironmentVariable("SYSCOM_SIMULATION_URL")!);
        if (!url.IsLoopback || url.Scheme != "http")
            throw new InvalidOperationException("Simulation must use local HTTP.");
        var http = new HttpClient { BaseAddress = url };
        http.DefaultRequestHeaders.Add("x-mock-slug", "syscom");
        return http;
    }

    private static SyscomClient Client(HttpClient http) => new(http,
        new SyscomClientOptions("fixture-client", "fixture-secret")
        {
            MaxGetAttempts = 4,
            GetRetryBaseDelay = TimeSpan.FromMilliseconds(1),
            GetRetryMaxDelay = TimeSpan.FromMilliseconds(10),
            RequestTimeout = TimeSpan.FromSeconds(3),
            MaxGetDuration = TimeSpan.FromSeconds(10)
        });

    private static async Task Reset(HttpClient http, string mode)
    {
        using var response = await http.PostAsync("/__control/reset?mode=" + mode, null);
        response.EnsureSuccessStatusCode();
    }

    [SimulationFact]
    public async Task TransientHttpFailures_Recover_AndRecalculateOtherCompanyPrices()
    {
        using var http = Http();
        await Reset(http, "transient");
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var uow = fixture.CreateUnitOfWork();
        await new SyscomWorker(NullLoggerFactory.Instance, Client(http), uow).RunAsync(CancellationToken.None);
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_runs WHERE status='Succeeded' AND pages_loaded=2"));
        Assert.Equal(2, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products WHERE code LIKE 'SIM-%'"));
        Assert.Equal(6, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id IN (10,11)"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices p JOIN portfolio_products x ON x.id=p.portfolio_product_id WHERE x.company_id=2 AND p.portfolio_price_list_id=10 AND p.price=110"));
        using var state = await http.GetFromJsonAsync<JsonDocument>("/__control/state");
        Assert.Equal(2, state!.RootElement.GetProperty("tokens").GetInt32());
        Assert.Equal(2, state.RootElement.GetProperty("counts").GetProperty("GET /api/v1/categorias?").GetInt32());
        Assert.Equal(3, state.RootElement.GetProperty("counts").GetProperty("GET /api/v1/productos?categoria=1&pagina=1&limit=500").GetInt32());
    }

    [SimulationFact]
    public async Task FailureOnLaterPage_DoesNotPromotePartialCatalogOrReplacePrices()
    {
        using var http = Http();
        await Reset(http, "fail-page");
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var uow = fixture.CreateUnitOfWork();
        await Assert.ThrowsAsync<SyscomClientException>(() =>
            new SyscomWorker(NullLoggerFactory.Instance, Client(http), uow).RunAsync(CancellationToken.None));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_runs WHERE status='Failed'"));
        Assert.Equal(0, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products WHERE code LIKE 'SIM-%'"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id=10 AND price=7"));
    }

    [SimulationFact]
    public async Task SubsequentCatalog_RecordsMissing_WithoutReusingOldStagingOrDeletingProducts()
    {
        using var http = Http();
        await Reset(http, "normal");
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using (var first = fixture.CreateUnitOfWork())
            await new SyscomWorker(NullLoggerFactory.Instance, Client(http), first).RunAsync(CancellationToken.None);
        await Reset(http, "missing");
        await using (var second = fixture.CreateUnitOfWork())
            await new SyscomWorker(NullLoggerFactory.Instance, Client(http), second).RunAsync(CancellationToken.None);
        Assert.Equal(2, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_runs WHERE status='Succeeded'"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_missing WHERE code='SIM-2'"));
        Assert.Equal(2, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products WHERE code LIKE 'SIM-%'"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT products_staged FROM syscom_sync_runs ORDER BY started_at DESC LIMIT 1"));
    }
    [SimulationFact]
    public async Task IncompleteAdvertisedTotal_DoesNotPromoteEvenWithAdvancingPages()
    {
        using var http = Http();
        await Reset(http, "incomplete");
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var uow = fixture.CreateUnitOfWork();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SyscomWorker(NullLoggerFactory.Instance, Client(http), uow).RunAsync(CancellationToken.None));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM syscom_sync_runs WHERE status='Failed'"));
        Assert.Equal(0, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_products WHERE code LIKE 'SIM-%'"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT count(*) FROM portfolio_prices WHERE portfolio_price_list_id=10 AND price=7"));
    }
}
