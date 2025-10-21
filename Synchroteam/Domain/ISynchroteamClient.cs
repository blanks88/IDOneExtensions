namespace Synchroteam.Domain;

public interface ISynchroteamClient : IDisposable
{
    ICustomersClient Customers { get; }
}
