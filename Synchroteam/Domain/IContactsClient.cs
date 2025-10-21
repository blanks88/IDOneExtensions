namespace Synchroteam.Domain;

public interface IContactsClient
{
    Task<ContactDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<ContactDto?> GetByMyIdAsync(string myId, CancellationToken ct = default);
    Task<PagedResult<ContactDto>> ListAsync(DateTimeOffset? updatedSince = null, int page = 1, int pageSize = 100, CancellationToken ct = default);
    Task<ContactDto?> UpsertAsync(ContactDto contact, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
