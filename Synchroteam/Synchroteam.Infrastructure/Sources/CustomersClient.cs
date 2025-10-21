using Synchroteam.Domain;

namespace Synchroteam.Infrastructure.Sources;

internal sealed class CustomersClient(SynchroteamHttpClient http) : ICustomersClient
{
    public Task<CustomerDto?> GetByIdAsync(int id, CancellationToken ct = default)
        => http.GetAsync<CustomerDto>($"customers/{id}", null, ct);

    public async Task<CustomerDto?> GetByMyIdAsync(string myId, CancellationToken ct = default)
    {
        var result = await http.GetAsync<PagedResult<CustomerDto>>("customers", new()
        {
            ["myId"] = myId,
            ["page"] = "1",
            ["pageSize"] = "1"
        }, ct).ConfigureAwait(false);
        return result?.Items.FirstOrDefault();
    }

    public async Task<PagedResult<CustomerDto>> ListAsync(DateTimeOffset? updatedSince = null, int page = 1, int pageSize = 100, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (updatedSince.HasValue)
            query["updatedSince"] = updatedSince.Value.UtcDateTime.ToString("o");
        var res = await http.GetAsync<PagedResult<CustomerDto>>("customers", query, ct).ConfigureAwait(false);
        return res ?? new PagedResult<CustomerDto>([], page, pageSize, 0);
    }

    public Task<CustomerDto?> UpsertAsync(CustomerDto customer, CancellationToken ct = default)
    {
        if (customer.Id is > 0)
            return http.PutAsync<CustomerDto, CustomerDto>($"customers/{customer.Id}", customer, ct);
        return http.PostAsync<CustomerDto, CustomerDto>("customers", customer, ct);
    }

    public Task DeleteAsync(int id, CancellationToken ct = default)
        => http.DeleteAsync($"customers/{id}", ct);
}
