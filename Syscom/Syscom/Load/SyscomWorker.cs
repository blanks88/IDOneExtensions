using System.Runtime.CompilerServices;
using IDOneRepository;
using Microsoft.Extensions.Logging;
using Syscom.Models;

namespace Syscom.Load;

public class SyscomWorker(ILoggerFactory loggerFactory, ISyscomClient client, IUnitOfWork uow)
{
    private readonly ILogger<SyscomWorker> _logger = loggerFactory.CreateLogger<SyscomWorker>();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        const int companyId = 1;
        var database = new SyscomDatabaseWorker(loggerFactory.CreateLogger<SyscomDatabaseWorker>(), uow);
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

    private async IAsyncEnumerable<(SyscomCategory category, List<SyscomProduct> products)> LoadProductsByCategoryAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var categories = await client.GetCategoriesAsync(cancellationToken);
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

                page = resp.PaginaActual + 1;
            } while (page <= total && !cancellationToken.IsCancellationRequested);

            yield return (category, products);
        }
    }
    
}