namespace Synchroteam.Models;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public record CustomerDto
{
    public int? Id { get; init; }
    public string? MyId { get; init; }
    public string? Name { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public AddressDto? Address { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public record SiteDto
{
    public int? Id { get; init; }
    public string? MyId { get; init; }
    public string? Name { get; init; }
    public int? CustomerId { get; init; }
    public AddressDto? Address { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public record ContactDto
{
    public int? Id { get; init; }
    public string? MyId { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public int? CustomerId { get; init; }
    public int? SiteId { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public record AddressDto
{
    public string? Line1 { get; init; }
    public string? Line2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
}
