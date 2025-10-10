using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Synchroteam.Interfaces;

namespace Synchroteam.DependencyInjection;

public static class SynchroteamServiceCollectionExtensions
{
    public static IServiceCollection AddSynchroteamClient(this IServiceCollection services, Action<SynchroteamClientOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<ISynchroteamClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SynchroteamClientOptions>>().Value;
            return new SynchroteamClient(options);
        });
        return services;
    }
}
