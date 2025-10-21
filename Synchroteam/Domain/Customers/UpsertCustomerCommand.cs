using MediatR;

namespace Synchroteam.Domain.Customers;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed record UpsertCustomerCommand(
    int? Id,
    string? MyId,
    string? Name,
    string? Email,
    string? Phone,
    AddressDto? Address
) : IRequest<CustomerDto>
{
    public static implicit operator CustomerDto(UpsertCustomerCommand src)
        => new()
        {
            Id = src.Id,
            MyId = src.MyId,
            Name = src.Name,
            Email = src.Email,
            Phone = src.Phone,
            Address = src.Address is null
                ? null
                : new AddressDto
                {
                    Line1 = src.Address.Line1,
                    Line2 = src.Address.Line2,
                    City = src.Address.City,
                    State = src.Address.State,
                    PostalCode = src.Address.PostalCode,
                    Country = src.Address.Country
                }
        };
}