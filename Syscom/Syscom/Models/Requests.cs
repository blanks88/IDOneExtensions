namespace Syscom.Models;

// Parameter records for SyscomClient methods
public record SyscomAccessTokenRequest(string ClientId, string ClientSecret);

public record SyscomBillsRequest(int? Anio = null, string? Busqueda = null, int? Pagina = null);

public record SyscomFacturaDetalleRequest(string Id);

public record SyscomProductsRequest(
    int? Categoria = null,
    string? Marca = null,
    string? Busqueda = null,
    string? Sucursal = null,
    string? Orden = null,
    bool? Stock = null,
    bool? Agrupar = null,
    int? Pagina = null
);

public record SyscomProductInfoRequest(string Id);

public record SyscomRelatedProductsRequest(string Id);

public record SyscomProductsAccesoriesRequest(string Id);