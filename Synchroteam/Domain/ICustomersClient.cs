namespace Synchroteam.Domain;

public interface ICustomersClient
{
    Task<SynchroteamCustomer?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<SynchroteamCustomer?> GetByMyIdAsync(string myId, CancellationToken ct = default);
    Task<SynchroteamPagedResult<SynchroteamCustomer>> ListAsync(DateTimeOffset? changedSince = null, int page = 1, int pageSize = 100, CancellationToken ct = default);
    Task<SynchroteamCustomer?> UpsertAsync(SynchroteamCustomer customer, CancellationToken ct = default);
    Task DeleteByIdAsync(int id, CancellationToken ct = default);
    Task DeleteByMyIdAsync(int id, CancellationToken ct = default);
}
