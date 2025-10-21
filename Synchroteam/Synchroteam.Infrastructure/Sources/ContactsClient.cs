using Synchroteam.Domain;

namespace Synchroteam.Infrastructure.Sources;

internal sealed class ContactsClient(SynchroteamHttpClient http) : IContactsClient
{
    public Task<ContactDto?> GetByIdAsync(int id, CancellationToken ct = default)
        => http.GetAsync<ContactDto>($"contacts/{id}", null, ct);

    public async Task<ContactDto?> GetByMyIdAsync(string myId, CancellationToken ct = default)
    {
        var result = await http.GetAsync<PagedResult<ContactDto>>("contacts", new()
        {
            ["myId"] = myId,
            ["page"] = "1",
            ["pageSize"] = "1"
        }, ct).ConfigureAwait(false);
        return result?.Items.FirstOrDefault();
    }

    public async Task<PagedResult<ContactDto>> ListAsync(DateTimeOffset? updatedSince = null, int page = 1,
        int pageSize = 100, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (updatedSince.HasValue)
            query["updatedSince"] = updatedSince.Value.UtcDateTime.ToString("o");
        var res = await http.GetAsync<PagedResult<ContactDto>>("contacts", query, ct).ConfigureAwait(false);
        return res ?? new PagedResult<ContactDto>([], page, pageSize, 0);
    }

    public Task<ContactDto?> UpsertAsync(ContactDto contact, CancellationToken ct = default)
    {
        return contact.Id is > 0
            ? http.PutAsync<ContactDto, ContactDto>($"contacts/{contact.Id}", contact, ct)
            : http.PostAsync<ContactDto, ContactDto>("contacts", contact, ct);
    }

    public Task DeleteAsync(int id, CancellationToken ct = default)
        => http.DeleteAsync($"contacts/{id}", ct);
}