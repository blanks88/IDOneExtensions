using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Synchroteam.Domain;
using Synchroteam.Infrastructure.V3;

namespace Synchroteam.Infrastructure;

public static class Injector
{
    public static IServiceCollection AddSynchroteamClient(this IServiceCollection services, ConfigurationManager cfg)
    {
        services.AddSingleton<ISynchroteamClient>(_ =>
        {
            var baseUrl = cfg["SYNCHROTEAM_BASE_URL"] ?? cfg["Synchroteam:BaseUrl"];
            var apiKey = cfg["SYNCHROTEAM_API_KEY"] ?? cfg["Synchroteam:ApiKey"];
            var domain = cfg["SYNCHROTEAM_DOMAIN"] ?? cfg["Synchroteam:Domain"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new InvalidOperationException("SYNCHROTEAM_BASE_URL (or Synchroteam:BaseUrl) is not configured");
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("SYNCHROTEAM_API_KEY (or Synchroteam:ApiKey) is not configured");
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("SYNCHROTEAM_DOMAIN (or Synchroteam:Domain) is not configured");
            }

            return new SynchroteamClient(
                new SynchroteamClientOptions
                {
                    BaseUrl = new Uri(baseUrl),
                    ApiKey = apiKey,
                    Domain = domain
                }
            );
        });

        return services;
    }
}