using MediatR;
using Synchroteam.Domain.Customers;

namespace Synchroteam.Middleware.Handlers;

public static class SyncCustomerToSynchroteamHandler
{
    /// <summary>
    /// Minimal push sync endpoint for Customer (Phase 1)
    /// POST /sync-to-st/customer
    /// Accepts a minimal request and upserts to Synchroteam.Infrastructure using CQRS (MediatR)
    /// </summary>
    public static async Task<IResult> HandleAsync(UpsertCustomerCommand command, 
        IMediator mediator, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct).ConfigureAwait(false);
        return Results.Ok(result);
    }
}