using MediatR;

namespace Synchroteam.Domain.Customers;

public sealed class UpsertCustomerCommandHandler(ISynchroteamClient client)
    : IRequestHandler<UpsertCustomerCommand, SynchroteamCustomer?>
{
    public async Task<SynchroteamCustomer?> Handle(UpsertCustomerCommand command, CancellationToken cancellationToken)
    {
        return await client.Customers.UpsertAsync(command, cancellationToken).ConfigureAwait(false);
    }
}