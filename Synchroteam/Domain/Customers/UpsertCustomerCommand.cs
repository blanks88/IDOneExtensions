using MediatR;

namespace Synchroteam.Domain.Customers;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed record UpsertCustomerCommand(
    int SynchroteamId,
    string SourceId,
    string Name,
    string AddressComplement,
    string AddressCity,
    string AddressCountry,
    string AddressProvince,
    string AddressStreet,
    string AddressZip,
    string ContactEmail,
    string ContactFirstName,
    string ContactFax,
    string ContactPhone,
    string ContactLastName,
    string VatNumber,
    Uri PublicLink,
    SynchroteamCustomerPosition Position,
    List<string> Tags,
    bool Active
) : IRequest<SynchroteamCustomer>
{
    public static implicit operator SynchroteamCustomer(UpsertCustomerCommand src)
        => new()
        {
            SynchroteamId = src.SynchroteamId, 
            SourceId = src.SourceId, 
            Name = src.Name, 
            AddressComplement = src.AddressComplement, 
            AddressCity = src.AddressCity, 
            AddressCountry = src.AddressCountry, 
            AddressProvince = src.AddressProvince, 
            AddressStreet = src.AddressStreet, 
            AddressZip = src.AddressZip, 
            ContactEmail = src.ContactEmail, 
            ContactFirstName = src.ContactFirstName, 
            ContactFax = src.ContactFax, 
            ContactPhone = src.ContactPhone, 
            ContactLastName = src.ContactLastName, 
            VatNumber = src.VatNumber, 
            PublicLink = src.PublicLink, 
            Position = src.Position, 
            Tags = src.Tags,
            Active = src.Active, 
        };
}