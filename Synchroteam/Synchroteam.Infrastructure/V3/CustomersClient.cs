using Synchroteam.Domain;

namespace Synchroteam.Infrastructure.V3;

internal sealed class CustomersClient(SynchroteamHttpClient http) : ICustomersClient
{
    public async Task<SynchroteamCustomer?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?> { ["id"] = $"{id}" };
        return await http.GetAsync<SynchroteamCustomer>("customer/details", query, ct);
    }

    public async Task<SynchroteamCustomer?> GetByMyIdAsync(string myId, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?> { ["myId"] = $"{myId}" };
        return await http.GetAsync<SynchroteamCustomer>("customer/details", query, ct);
    }

    public async Task<SynchroteamPagedResult<SynchroteamCustomer>> ListAsync(
        DateTimeOffset? changedSince = null, int page = 1,
        int pageSize = 100, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };

        if (changedSince.HasValue)
        {
            query["changedSince"] = changedSince.Value.UtcDateTime.ToString("o");
        }

        var res = await http.GetAsync<SynchroteamPagedResult<SynchroteamCustomer>>(
            "customer/list", query, ct).ConfigureAwait(false);

        return res ?? SynchroteamPagedResult<SynchroteamCustomer>.Empty;
    }

    public async Task<SynchroteamCustomer?> UpsertAsync(SynchroteamCustomer customer, CancellationToken ct = default)
    {
        return await http.PostAsync<SynchroteamCustomer, SynchroteamCustomer>("customer/send", customer, ct);
    }

    public Task DeleteByIdAsync(int id, CancellationToken ct = default)
        => http.DeleteAsync($"customer/delete?id={id}", ct);
    public Task DeleteByMyIdAsync(int myId, CancellationToken ct = default)
        => http.DeleteAsync($"customer/delete?myId={myId}", ct);
}