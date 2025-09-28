using System.Text.Json;
using Syscom.Models;

namespace Syscom;

public interface ISyscomClient
{
    // OAuth
    Task<SyscomAccessTokenResponse> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    // Categories
    Task<List<SyscomCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<SyscomCategory> GetCategoriyByIdAsync(int id, CancellationToken cancellationToken = default);

    // Bills
    Task<JsonDocument> GetBillsAsync(SyscomBillsRequest parameters, CancellationToken cancellationToken = default);
    Task<JsonDocument> GetBillDetailAsync(SyscomFacturaDetalleRequest parameters, CancellationToken cancellationToken = default);

    // Products
    Task<SyscomProductsSearchResponse> GetProductsAsync(SyscomProductsRequest parameters, CancellationToken cancellationToken = default);
    Task<SyscomProduct> GetProductInfoAsync(SyscomProductInfoRequest parameters, CancellationToken cancellationToken = default);
    Task<JsonDocument> GetRelatedProductsAsync(SyscomRelatedProductsRequest parameters, CancellationToken cancellationToken = default);
    Task<JsonDocument> GetProductAccesoriesAsync(SyscomProductsAccesoriesRequest parameters, CancellationToken cancellationToken = default);
}