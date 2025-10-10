using Synchroteam.Clients;
using Synchroteam.Interfaces;
using Synchroteam.Internal.Http;

namespace Synchroteam;

public sealed class SynchroteamClient : ISynchroteamClient
{
    private readonly SynchroteamHttpClient _http;

    public SynchroteamClientOptions Options { get; }
    public ICustomersClient Customers { get; }
    public ISitesClient Sites { get; }
    public IContactsClient Contacts { get; }

    public SynchroteamClient(SynchroteamClientOptions options)
    {
        _http = new SynchroteamHttpClient(options);
        Customers = new CustomersClient(_http);
        Contacts = new ContactsClient(_http);
        Sites = new SitesClient(_http);
        Options = options;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
