using System.Text.Json.Serialization;

namespace Synchroteam.Domain;

public partial class SynchroteamPagedResult<T>
{
    [JsonPropertyName("page")] public int Page { get; set; } = 1;

    [JsonPropertyName("pageSize")] public int PageSize { get; set; }

    [JsonPropertyName("recordsTotal")] public int RecordsTotal { get; set; }

    [JsonPropertyName("data")] public IReadOnlyList<T> Data { get; set; } = [];

    public bool HasNextPage => Page < RecordsTotal / PageSize + 1;

    public static SynchroteamPagedResult<T> Empty => new();
}

public partial class SynchroteamCustomer
{
    [JsonPropertyName("id")] public int SynchroteamId { get; set; }

    [JsonPropertyName("myId")] public string SourceId { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; }

    [JsonPropertyName("address")]
    public string Address =>
        $"{AddressStreet}, {AddressCity}, {AddressProvince}, {AddressZip}, {AddressCountry}, {AddressComplement}";

    [JsonPropertyName("addressComplement")]
    public string AddressComplement { get; set; }

    [JsonPropertyName("addressCity")] public string AddressCity { get; set; }

    [JsonPropertyName("addressCountry")] public string AddressCountry { get; set; }

    [JsonPropertyName("addressProvince")] public string AddressProvince { get; set; }

    [JsonPropertyName("addressStreet")] public string AddressStreet { get; set; }

    [JsonPropertyName("addressZIP")] public string AddressZip { get; set; }

    [JsonPropertyName("contactEmail")] public string ContactEmail { get; set; }

    [JsonPropertyName("contactFirstName")] public string ContactFirstName { get; set; }

    [JsonPropertyName("contactFax")] public string ContactFax { get; set; }

    [JsonPropertyName("contactPhone")] public string ContactPhone { get; set; }

    [JsonPropertyName("contactLastName")] public string ContactLastName { get; set; }

    [JsonPropertyName("vatNumber")] public string VatNumber { get; set; }

    [JsonPropertyName("publicLink")] public Uri PublicLink { get; set; }

    [JsonPropertyName("customFieldValues")]
    public List<SynchroteamCustomerCustomFieldValue> CustomFieldValues { get; set; }

    [JsonPropertyName("position")] public SynchroteamCustomerPosition Position { get; set; }

    [JsonPropertyName("tags")] public List<string> Tags { get; set; }

    [JsonPropertyName("active")] public bool Active { get; set; }
}

public partial class SynchroteamCustomerCustomFieldValue
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("label")] public string Label { get; set; }

    [JsonPropertyName("value")] public string Value { get; set; }
}

public partial class SynchroteamCustomerPosition
{
    [JsonPropertyName("longitude")] public string Longitude { get; set; }

    [JsonPropertyName("latitude")] public string Latitude { get; set; }
}