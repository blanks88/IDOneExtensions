namespace Synchroteam.Domain;

public interface ISitesClient
{
    Task<SiteDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<SiteDto?> GetByMyIdAsync(string myId, CancellationToken ct = default);
    Task<PagedResult<SiteDto>> ListAsync(DateTimeOffset? updatedSince = null, int page = 1, int pageSize = 100, CancellationToken ct = default);
    Task<SiteDto?> UpsertAsync(SiteDto site, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
