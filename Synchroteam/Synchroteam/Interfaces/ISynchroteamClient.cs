namespace Synchroteam.Interfaces;

public interface ISynchroteamClient : IDisposable
{
    ICustomersClient Customers { get; }
    ISitesClient Sites { get; }
    IContactsClient Contacts { get; }
}
