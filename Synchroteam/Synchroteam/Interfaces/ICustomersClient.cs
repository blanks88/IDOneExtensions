using Synchroteam.Models;

namespace Synchroteam.Interfaces;

public interface ICustomersClient
{
    Task<CustomerDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<CustomerDto?> GetByMyIdAsync(string myId, CancellationToken ct = default);
    Task<PagedResult<CustomerDto>> ListAsync(DateTimeOffset? updatedSince = null, int page = 1, int pageSize = 100, CancellationToken ct = default);
    Task<CustomerDto?> UpsertAsync(CustomerDto customer, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
