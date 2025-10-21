using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Synchroteam.Domain;

namespace Synchroteam.Infrastructure;

public static class Injector
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
