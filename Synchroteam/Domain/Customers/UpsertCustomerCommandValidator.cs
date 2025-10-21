using System.Diagnostics.CodeAnalysis;
using FluentValidation;

namespace Synchroteam.Domain.Customers;

[SuppressMessage("ReSharper", "NullableWarningSuppressionIsUsed")]
// ReSharper disable once UnusedType.Global
public sealed class UpsertCustomerCommandValidator : AbstractValidator<UpsertCustomerCommand>
{
    public UpsertCustomerCommandValidator()
    {
        
    }
}
