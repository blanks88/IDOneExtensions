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
        if (_options.MaximumDuration <= TimeSpan.Zero || _options.RetentionDays < 1 || _options.CompanyId < 1 ||
            _options.PageSize is < 10 or > 1000 || _options.BufferedPages is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(options),
                "Sync duration, retention and company must be positive; page size must be 10..1000 and buffered pages 1..16.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.MaximumDuration);
        var ct = deadline.Token;
        var sw = Stopwatch.StartNew();
        var metrics = new SyscomSyncMetrics();
        var database = new SyscomDatabaseWorker(loggerFactory.CreateLogger<SyscomDatabaseWorker>(), uow, metrics);
        Guid? runId = null;
        _logger.LogInformation("Syscom sync started at {StartTime:o}.", DateTimeOffset.UtcNow);
        try
        {
            List<SyscomCategory> categories;
            using (metrics.Measure(SyscomSyncStage.Preparation))
                runId = await database.BeginRunAsync(_options.RetentionDays, _options.CompanyId, ct);

            using (metrics.Measure(SyscomSyncStage.Download))
                categories = await client.GetCategoriesAsync(ct);
            _logger.LogInformation("Syscom categories found: {Count}.", categories.Count);
            await SyscomPagePipeline.RunAsync(categories.OrderBy(c => c.Id).ToArray(), _options.BufferedPages,
                async (categoryId, page, token) =>
                {
                    using (metrics.Measure(SyscomSyncStage.Download))
                        return await client.GetProductsAsync(new SyscomProductsRequest
                        {
                            Categoria = checked((int)categoryId), Pagina = page, Limit = _options.PageSize
                        }, token);
                },
                async (categoryId, page, totalPages, products, token) =>
                {
                    await database.BulkInsertAsync(runId.Value, categoryId, products, token);
                    _logger.LogInformation("Syscom category {CategoryId} page {Page} of {TotalPages} staged.",
                        categoryId, page, totalPages);
                }, ct);

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
            _logger.LogError("Syscom sync failed at {EndTime:o}. Total time: {TotalTime}. Error type: {ErrorType}. Run: {RunId}. Reason: {Reason}.",
                DateTimeOffset.UtcNow, sw.Elapsed, ex.GetType().Name, runId, SyscomFailureDiagnostics.Describe(ex));
            throw;
        }
        finally
        {
            if (runId.HasValue)
            {
                try
                {
                    using var metricsCleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await database.RecordMetricsAsync(runId.Value, metricsCleanup.Token);
                }
                catch (Exception ex)
                {
                    try
                    {
                        _logger.LogWarning("SYSCOM metrics persistence failed. Error type: {ErrorType}.",
                            ex.GetType().Name);
                    }
                    catch { /* Instrumentation must not replace the sync result or block lock cleanup. */ }
                }
            }
            try
            {
                _logger.LogInformation(
                    "SYSCOM timing summary (ms): total={TotalMs}, preparation={PreparationMs}, download={DownloadMs}, staging={StagingMs}, catalog promotion={CatalogPromotionMs}, global price recalculation={PriceRecalculationMs}, promotion={PromotionMs}.",
                    metrics.TotalElapsedMilliseconds,
                    metrics.GetElapsedMilliseconds(SyscomSyncStage.Preparation),
                    metrics.GetElapsedMilliseconds(SyscomSyncStage.Download),
                    metrics.GetElapsedMilliseconds(SyscomSyncStage.Staging),
                    metrics.GetElapsedMilliseconds(SyscomSyncStage.CatalogPromotion),
                    metrics.GetElapsedMilliseconds(SyscomSyncStage.PriceRecalculation),
                    metrics.GetElapsedMilliseconds(SyscomSyncStage.Promotion));
            }
            catch { /* Instrumentation must not replace the sync result or block lock cleanup. */ }
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
