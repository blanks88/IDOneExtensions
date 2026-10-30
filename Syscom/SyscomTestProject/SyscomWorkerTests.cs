using IDOneRepository;
using IDOneRepository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Syscom;
using Syscom.Load;

namespace SyscomTestProject;

public sealed class SyscomIntegrationFactAttribute : FactAttribute
{
    public SyscomIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SYSCOM_RUN_INTEGRATION_TESTS") != "1")
            Skip = "Live SYSCOM integration tests require SYSCOM_RUN_INTEGRATION_TESTS=1.";
    }
}

public class SyscomWorkerTests
{
    [SyscomIntegrationFact]
    public async Task RunWorker_SyncsProducts()
    {
        if (Environment.GetEnvironmentVariable("SYSCOM_INTEGRATION_DATABASE_ISOLATED") != "1")
            throw new InvalidOperationException(
                "Set SYSCOM_INTEGRATION_DATABASE_ISOLATED=1 only after confirming an isolated test database.");

        var configuration = SyscomAppConfiguration.Parse(new Dictionary<string, string?>
        {
            ["CONNECTION_STRING"] = Required("SYSCOM_INTEGRATION_CONNECTION_STRING"),
            ["SYSCOM_CLIENT_ID"] = Required("SYSCOM_INTEGRATION_CLIENT_ID"),
            ["SYSCOM_CLIENT_SECRET"] = Required("SYSCOM_INTEGRATION_CLIENT_SECRET"),
            ["SYSCOM_COMPANY_ID"] = Required("SYSCOM_INTEGRATION_COMPANY_ID"),
            ["SYSCOM_BASE_URL"] = Environment.GetEnvironmentVariable("SYSCOM_INTEGRATION_BASE_URL"),
            ["SYSCOM_OAUTH_TOKEN_PATH"] = Environment.GetEnvironmentVariable("SYSCOM_INTEGRATION_OAUTH_TOKEN_PATH"),
            ["SYSCOM_OAUTH_FORM_ENCODED"] = Environment.GetEnvironmentVariable("SYSCOM_INTEGRATION_OAUTH_FORM_ENCODED")
        });

        try
        {
            using var httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            var client = new SyscomClient(httpClient,
                new SyscomClientOptions(configuration.ClientId, configuration.ClientSecret)
                {
                    BaseUrl = configuration.BaseUrl,
                    OAuthTokenPath = configuration.OAuthTokenPath,
                    UseFormEncodedTokenRequest = configuration.UseFormEncodedTokenRequest,
                    RequestTimeout = configuration.RequestTimeout
                });
            var dbOptions = new DbContextOptionsBuilder<IDOneDbContext>()
                .UseNpgsql(configuration.ConnectionString)
                .Options;
            await using var unitOfWork = new UnitOfWork(new IDOneDbContext(dbOptions));
            using var loggerFactory = LoggerFactory.Create(_ => { });
            var worker = new SyscomWorker(loggerFactory, client, unitOfWork,
                new SyscomSyncOptions
                {
                    CompanyId = configuration.CompanyId,
                    MaximumDuration = configuration.MaximumDuration,
                    RetentionDays = configuration.RetentionDays
                });

            await worker.RunAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // xUnit prints exception messages; do not expose provider response or DB details.
            throw new InvalidOperationException($"Integration sync failed ({ex.GetType().Name}).");
        }
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Missing required environment variable: {name}.");
    }
}
