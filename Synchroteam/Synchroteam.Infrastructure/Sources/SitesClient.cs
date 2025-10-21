using Synchroteam.Domain;

namespace Synchroteam.Infrastructure.Sources;

internal sealed class SitesClient(SynchroteamHttpClient http) : ISitesClient
{
    public Task<SiteDto?> GetByIdAsync(int id, CancellationToken ct = default)
        => http.GetAsync<SiteDto>($"sites/{id}", null, ct);

    public async Task<SiteDto?> GetByMyIdAsync(string myId, CancellationToken ct = default)
    {
        var result = await http.GetAsync<PagedResult<SiteDto>>("sites", new()
        {
            ["myId"] = myId,
            ["page"] = "1",
            ["pageSize"] = "1"
        }, ct).ConfigureAwait(false);
        return result?.Items.FirstOrDefault();
    }

    public async Task<PagedResult<SiteDto>> ListAsync(DateTimeOffset? updatedSince = null, int page = 1, int pageSize = 100, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (updatedSince.HasValue)
            query["updatedSince"] = updatedSince.Value.UtcDateTime.ToString("o");
        var res = await http.GetAsync<PagedResult<SiteDto>>("sites", query, ct).ConfigureAwait(false);
        return res ?? new PagedResult<SiteDto>([], page, pageSize, 0);
    }

    public Task<SiteDto?> UpsertAsync(SiteDto site, CancellationToken ct = default)
    {
        if (site.Id is > 0)
            return http.PutAsync<SiteDto, SiteDto>($"sites/{site.Id}", site, ct);
        return http.PostAsync<SiteDto, SiteDto>("sites", site, ct);
    }

    public Task DeleteAsync(int id, CancellationToken ct = default)
        => http.DeleteAsync($"sites/{id}", ct);
}
