using System.Diagnostics.CodeAnalysis;
using FluentValidation;

namespace Synchroteam.Domain.Customers;

[SuppressMessage("ReSharper", "NullableWarningSuppressionIsUsed")]
// ReSharper disable once UnusedType.Global
public sealed class UpsertCustomerCommandValidator : AbstractValidator<UpsertCustomerCommand>
{
    public UpsertCustomerCommandValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Name) || !string.IsNullOrWhiteSpace(x.MyId))
            .WithMessage("Either name or myId must be provided");

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));

        When(x => x.Address is not null, () =>
        {
            RuleFor(x => x.Address!.Line1).MaximumLength(200).When(x => !string.IsNullOrWhiteSpace(x.Address!.Line1));
            RuleFor(x => x.Address!.Line2).MaximumLength(200).When(x => !string.IsNullOrWhiteSpace(x.Address!.Line2));
            RuleFor(x => x.Address!.City).MaximumLength(100).When(x => !string.IsNullOrWhiteSpace(x.Address!.City));
            RuleFor(x => x.Address!.State).MaximumLength(100).When(x => !string.IsNullOrWhiteSpace(x.Address!.State));
            RuleFor(x => x.Address!.PostalCode).MaximumLength(20).When(x => !string.IsNullOrWhiteSpace(x.Address!.PostalCode));
            RuleFor(x => x.Address!.Country).MaximumLength(100).When(x => !string.IsNullOrWhiteSpace(x.Address!.Country));
        });
    }
}
