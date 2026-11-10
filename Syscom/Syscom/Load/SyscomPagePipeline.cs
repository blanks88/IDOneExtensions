using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Syscom.Models;

[assembly: InternalsVisibleTo("SyscomTestProject")]

namespace Syscom.Load;

internal static class SyscomPagePipeline
{
    private sealed record Page(long CategoryId, int Number, int TotalPages, List<SyscomProduct> Products);

    internal static async Task RunAsync(
        IReadOnlyList<SyscomCategory> categories,
        int bufferedPages,
        Func<long, int, CancellationToken, Task<SyscomProductsSearchResponse>> fetch,
        Func<long, int, int, List<SyscomProduct>, CancellationToken, Task> stage,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferedPages, 1);
        var channel = Channel.CreateBounded<Page>(new BoundedChannelOptions(bufferedPages)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = linked.Token;
        ExceptionDispatchInfo? firstFailure = null;

        void Stop(Exception error)
        {
            if (error is not OperationCanceledException || !ct.IsCancellationRequested)
                Interlocked.CompareExchange(ref firstFailure, ExceptionDispatchInfo.Capture(error), null);
            linked.Cancel();
        }

        async Task ProduceAsync()
        {
            try
            {
                foreach (var category in categories)
                {
                    var requestedPage = 1;
                    int? totalPages = null;
                    int? expectedProducts = null;
                    var receivedProducts = 0;
                    do
                    {
                        ct.ThrowIfCancellationRequested();
                        // One HTTP call at a time. While it runs, at most bufferedPages pages can
                        // wait in the channel and one page can be staged by the consumer.
                        var response = await fetch(category.Id, requestedPage, ct);
                        var emptyCatalogue = requestedPage == 1 && (response.Paginas is 0 or 1) &&
                            response.Total == 0 && response.Products is { Count: 0 };
                        SyscomFailureReason? paginationFailure = response.Products is null ? SyscomFailureReason.MissingProducts
                            : response.PaginaActual != requestedPage ? SyscomFailureReason.UnexpectedPage
                            : response.Paginas < requestedPage && !emptyCatalogue ? SyscomFailureReason.InvalidPageCount
                            : response.Products.Count == 0 && !emptyCatalogue ? SyscomFailureReason.EmptyPage
                            : totalPages.HasValue && totalPages.Value != response.Paginas ? SyscomFailureReason.PageCountChanged
                            : response.Total > 0 && expectedProducts.HasValue && expectedProducts.Value != response.Total
                                ? SyscomFailureReason.ProductTotalChanged : null;
                        if (paginationFailure.HasValue)
                            throw SyscomFailureDiagnostics.Validation(paginationFailure.Value,
                                category.Id, requestedPage, response.PaginaActual, totalPages, response.Paginas,
                                expectedProducts, response.Total);
                        totalPages = response.Paginas;
                        if (response.Total > 0) expectedProducts ??= response.Total;
                        receivedProducts = checked(receivedProducts + response.Products!.Count);
                        if (expectedProducts.HasValue && receivedProducts > expectedProducts.Value)
                            throw SyscomFailureDiagnostics.Validation(SyscomFailureReason.ProductCountExceeded,
                                category: category.Id, requestedPage: requestedPage,
                                expectedCount: expectedProducts, actualCount: receivedProducts);
                        await channel.Writer.WriteAsync(
                            new Page(category.Id, requestedPage, totalPages.Value, response.Products!), ct);
                        requestedPage++;
                    } while (requestedPage <= totalPages);
                    if (expectedProducts.HasValue && receivedProducts != expectedProducts.Value)
                        throw SyscomFailureDiagnostics.Validation(SyscomFailureReason.ProductCountMismatch,
                            category: category.Id, requestedPage: requestedPage - 1,
                            expectedCount: expectedProducts, actualCount: receivedProducts);
                }
                channel.Writer.TryComplete();
            }
            catch (Exception error)
            {
                Stop(error);
                channel.Writer.TryComplete(error);
                throw;
            }
        }

        async Task ConsumeAsync()
        {
            try
            {
                await foreach (var page in channel.Reader.ReadAllAsync(ct))
                {
                    try { await stage(page.CategoryId, page.Number, page.TotalPages, page.Products, ct); }
                    catch (Exception error)
                    {
                        SyscomFailureDiagnostics.AddPageContext(error, page.CategoryId, page.Number);
                        throw;
                    }
                }
            }
            catch (Exception error)
            {
                Stop(error);
                throw;
            }
        }

        // Both tasks are observed before the caller may promote or clean up the run.
        try { await Task.WhenAll(ProduceAsync(), ConsumeAsync()); }
        catch
        {
            firstFailure?.Throw();
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }
}
