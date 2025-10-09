using System.Runtime.CompilerServices;
using System.Diagnostics;
using IDOneRepository;
using Microsoft.Extensions.Logging;
using Syscom.Models;

namespace Syscom.Load;

public class SyscomWorker(ILoggerFactory loggerFactory, ISyscomClient client, IUnitOfWork uow)
{
    private readonly ILogger<SyscomWorker> _logger = loggerFactory.CreateLogger<SyscomWorker>();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.Now;
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Syscom sync started at {StartTime:o}.", startTime);

        try
        {
            const int companyId = 1;
            var database = new SyscomDatabaseWorker(loggerFactory.CreateLogger<SyscomDatabaseWorker>(), uow);
            _logger.LogInformation("Preparing Syscom sync process.");
            await database.CreateStagingTableAsync(cancellationToken);

            _logger.LogInformation("Starting Syscom landing process.");

            await foreach (var (category, products) in LoadProductsByCategoryAsync(cancellationToken))
            {
                // COPY to staging via UoW connection
                _logger.LogInformation("Processing category {CategoryId} - {CategoryName} with {Count} products.", category.Id, category.Nombre, products.Count);
                await database.BulkInsertAsync(products, cancellationToken);
            }

            // Import SQL (SAT, measurements, portfolio products) from a staging table
            _logger.LogInformation("Syncing SAT, measurements and products into database.");
            await database.ExecSyncAsync(companyId, cancellationToken);
        }
        finally
        {
            sw.Stop();
            var endTime = DateTimeOffset.Now;
            _logger.LogInformation("Syscom sync finished at {EndTime:o}. Total time: {TotalTime}.", endTime, sw.Elapsed);
        }
    }

    private async IAsyncEnumerable<(SyscomCategory category, List<SyscomProduct> products)> LoadProductsByCategoryAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var categories = await client.GetCategoriesAsync(cancellationToken);
        _logger.LogInformation("  > {Count} categories found.", categories.Count);
        foreach (var category in categories)
        {
            var products = new List<SyscomProduct>();
            int page = 1, total;
            do
            {
                var resp = await client.GetProductsAsync(
                    new SyscomProductsRequest { Categoria = (int)category.Id, Pagina = page }, cancellationToken);

                total = resp.Paginas;
                if (resp.Products?.Count > 0)
                {
                    products.AddRange(resp.Products);
                }

                _logger.LogInformation("  > {Category} category: {Page} of {TotalPages} landed.", category.Nombre, resp.PaginaActual, total);
                page = resp.PaginaActual + 1;
            } while (page <= total && !cancellationToken.IsCancellationRequested);

            yield return (category, products);
        }
    }
    
}