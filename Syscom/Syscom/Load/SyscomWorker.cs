using System.Diagnostics;
using IDOneRepository;
using Microsoft.Extensions.Logging;
using Syscom.Models;

namespace Syscom.Load;

public class SyscomWorker(ILoggerFactory loggerFactory, ISyscomClient client, IUnitOfWork uow,
    SyscomSyncOptions? options = null)
{
    private readonly ILogger<SyscomWorker> _logger = loggerFactory.CreateLogger<SyscomWorker>();
    private readonly SyscomSyncOptions _options = options ?? new SyscomSyncOptions();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_options.MaximumDuration <= TimeSpan.Zero || _options.RetentionDays < 1 || _options.CompanyId < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Sync duration, retention and company must be positive.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.MaximumDuration);
        var ct = deadline.Token;
        var sw = Stopwatch.StartNew();
        var database = new SyscomDatabaseWorker(loggerFactory.CreateLogger<SyscomDatabaseWorker>(), uow);
        Guid? runId = null;
        _logger.LogInformation("Syscom sync started at {StartTime:o}.", DateTimeOffset.UtcNow);
        try
        {
            runId = await database.BeginRunAsync(_options.RetentionDays, _options.CompanyId, ct);
            var categories = await client.GetCategoriesAsync(ct);
            _logger.LogInformation("Syscom categories found: {Count}.", categories.Count);
            foreach (var category in categories.OrderBy(c => c.Id))
            {
                var requestedPage = 1;
                int? totalPages = null;
                int? expectedProducts = null;
                var receivedProducts = 0;
                do
                {
                    ct.ThrowIfCancellationRequested();
                    var response = await client.GetProductsAsync(
                        new SyscomProductsRequest { Categoria = checked((int)category.Id), Pagina = requestedPage }, ct);
                    var emptyCatalogue = requestedPage == 1 && (response.Paginas is 0 or 1) &&
                        response.Total == 0 && response.Products is { Count: 0 };
                    if (response.Products is null || response.PaginaActual != requestedPage ||
                        (response.Paginas < requestedPage && !emptyCatalogue) ||
                        (response.Products.Count == 0 && !emptyCatalogue) ||
                        (totalPages.HasValue && totalPages.Value != response.Paginas) ||
                        (response.Total > 0 && expectedProducts.HasValue &&
                         expectedProducts.Value != response.Total))
                        throw new InvalidOperationException(
                            $"SYSCOM pagination did not advance for category {category.Id}, requested page {requestedPage}.");
                    totalPages = response.Paginas;
                    if (response.Total > 0) expectedProducts ??= response.Total;
                    receivedProducts = checked(receivedProducts + response.Products.Count);
                    if (expectedProducts.HasValue && receivedProducts > expectedProducts.Value)
                        throw new InvalidOperationException(
                            $"SYSCOM product count exceeded the declared total for category {category.Id}.");
                    await database.BulkInsertAsync(runId.Value, category.Id, response.Products, ct);
                    _logger.LogInformation("Syscom category {CategoryId} page {Page} of {TotalPages} staged.",
                        category.Id, requestedPage, totalPages);
                    requestedPage++;
                } while (requestedPage <= totalPages);
                if (expectedProducts.HasValue && receivedProducts != expectedProducts.Value)
                    throw new InvalidOperationException(
                        $"SYSCOM product count did not match the declared total for category {category.Id}.");
            }

            await database.CompleteDownloadAsync(runId.Value, ct);
            await database.ExecSyncAsync(runId.Value, _options.CompanyId, ct);
            _logger.LogInformation("Syscom sync completed successfully at {EndTime:o}. Total time: {TotalTime}.",
                DateTimeOffset.UtcNow, sw.Elapsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (runId.HasValue) await TryFailRunAsync(database, runId.Value, "Cancelled");
            _logger.LogWarning("Syscom sync canceled at {EndTime:o}. Total time: {TotalTime}.", DateTimeOffset.UtcNow, sw.Elapsed);
            throw;
        }
        catch (Exception ex)
        {
            if (runId.HasValue) await TryFailRunAsync(database, runId.Value, "Failed");
            _logger.LogError("Syscom sync failed at {EndTime:o}. Total time: {TotalTime}. Error type: {ErrorType}.",
                DateTimeOffset.UtcNow, sw.Elapsed, ex.GetType().Name);
            throw;
        }
        finally
        {
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await database.ReleaseLockAsync(cleanup.Token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("SYSCOM lock cleanup failed. Error type: {ErrorType}.", ex.GetType().Name);
            }
        }
    }

    private async Task TryFailRunAsync(SyscomDatabaseWorker database, Guid runId, string status)
    {
        try
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await database.FailRunAsync(runId, status, cleanup.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("SYSCOM result persistence failed. Error type: {ErrorType}.", ex.GetType().Name);
        }
    }
}
