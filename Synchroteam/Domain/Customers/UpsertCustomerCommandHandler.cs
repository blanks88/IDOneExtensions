using MediatR;

namespace Synchroteam.Domain.Customers;

public sealed class UpsertCustomerCommandHandler(ISynchroteamClient client)
    : IRequestHandler<UpsertCustomerCommand, CustomerDto?>
{
    public async Task<CustomerDto?> Handle(UpsertCustomerCommand command, CancellationToken cancellationToken)
    {
        return await client.Customers.UpsertAsync(command, cancellationToken).ConfigureAwait(false);
    }
}