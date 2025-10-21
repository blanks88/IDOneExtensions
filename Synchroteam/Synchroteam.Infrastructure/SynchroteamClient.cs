using Synchroteam.Domain;
using Synchroteam.Infrastructure.V3;

namespace Synchroteam.Infrastructure;

public sealed class SynchroteamClient : ISynchroteamClient
{
    private readonly SynchroteamHttpClient _http;

    public SynchroteamClientOptions Options { get; }
    public ICustomersClient Customers { get; }
    public SynchroteamClient(SynchroteamClientOptions options)
    {
        _http = new SynchroteamHttpClient(options);
        Customers = new CustomersClient(_http);
        Options = options;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
