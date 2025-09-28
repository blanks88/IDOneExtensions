using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Syscom.Models;

namespace Syscom;

public class SyscomClient : ISyscomClient
{
    private readonly HttpClient _http;
    private readonly SyscomClientOptions _options;
    private readonly ILogger<SyscomClient>? _logger;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public SyscomClient(HttpClient httpClient, SyscomClientOptions options, ILogger<SyscomClient>? logger = null)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;

        if (_http.BaseAddress == null && !string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _http.BaseAddress = new Uri(_options.BaseUrl);
        }

        if (!string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
            _logger?.LogDebug("Authorization header set from provided AccessToken.");
        }

        _logger?.LogDebug("SyscomClient initialized with BaseAddress {BaseAddress}", _http.BaseAddress);
    }

    public async Task<SyscomAccessTokenResponse> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            client_id = _options.ClientId,
            client_secret = _options.ClientSecret,
            grant_type = "client_credentials"
        };

        const string path = "/oauth/token";

        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        _logger?.LogInformation("Requesting SYSCOM access token at {Path}", path);
        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Access token response status: {StatusCode}", (int)resp.StatusCode);
        await EnsureSuccess(resp, cancellationToken).ConfigureAwait(false);
        var token = await ReadAs<SyscomAccessTokenResponse>(resp, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(token.AccessToken))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            _options.AccessToken = token.AccessToken;
            _logger?.LogInformation("Access token obtained and applied to default Authorization header.");
        }
        else
        {
            _logger?.LogWarning("Access token response did not include an access_token value.");
        }

        return token;
    }

    public async Task<List<SyscomCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
        var url = Combine(_options.ApiBasePath, "/categorias");
        _logger?.LogDebug("GET {Url}", url);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Response {StatusCode} for GET {Url}", (int)resp.StatusCode, url);
        await EnsureSuccess(resp, cancellationToken).ConfigureAwait(false);
        return await ReadAs<List<SyscomCategory>>(resp, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SyscomCategory> GetCategoriyByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
        var url = Combine(_options.ApiBasePath, $"/categorias/{id}");
        _logger?.LogDebug("GET {Url}", url);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Response {StatusCode} for GET {Url}", (int)resp.StatusCode, url);
        await EnsureSuccess(resp, cancellationToken).ConfigureAwait(false);
        return await ReadAs<SyscomCategory>(resp, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JsonDocument> GetBillsAsync(SyscomBillsRequest parameters,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
        var url = Combine(_options.ApiBasePath, "/facturas");
        var qb = new List<string>();
        if (parameters.Anio.HasValue) qb.Add($"anio={parameters.Anio.Value}");
        if (!string.IsNullOrWhiteSpace(parameters.Busqueda))
            qb.Add($"busqueda={Uri.EscapeDataString(parameters.Busqueda)}");
        if (parameters.Pagina.HasValue) qb.Add($"pagina={parameters.Pagina.Value}");
        if (qb.Count > 0) url += "?" + string.Join("&", qb);

        _logger?.LogDebug("GET {Url}", url);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Response {StatusCode} for GET {Url}", (int)resp.StatusCode, url);
        await EnsureSuccess(resp, cancellationToken).ConfigureAwait(false);
        return await ReadJson(resp, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JsonDocument> GetBillDetailAsync(SyscomFacturaDetalleRequest parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrWhiteSpace(parameters.Id))
        {
            throw new SyscomClientException($" Value cannot be null or whitespace: {nameof(parameters.Id)}");
        }

        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
        var url = Combine(_options.ApiBasePath, "/facturas") + $"?id={Uri.EscapeDataString(parameters.Id)}";
        _logger?.LogDebug("GET {Url}", url);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Response {StatusCode} for GET {Url}", (int)resp.StatusCode, url);
        await EnsureSuccess(resp, cancellationToken).ConfigureAwait(false);
        return await ReadJson(resp, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SyscomProductsSearchResponse> GetProductsAsync(SyscomProductsRequest parameters,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
        var url = Combine(_options.ApiBasePath, "/productos");
        var qb = new List<string>();
        if (parameters.Categoria.HasValue) qb.Add($"categoria={parameters.Categoria.Value}");
        if (!string.IsNullOrWhiteSpace(parameters.Marca)) qb.Add($"marca={Uri.EscapeDataString(parameters.Marca)}");
        if (!string.IsNullOrWhiteSpace(parameters.Busqueda))
            qb.Add($"busqueda={Uri.EscapeDataString(parameters.Busqueda)}");
        if (!string.IsNullOrWhiteSpace(parameters.Sucursal))
            qb.Add($"sucursal={Uri.EscapeDataString(parameters.Sucursal)}");
        if (!string.IsNullOrWhiteSpace(parameters.Orden)) qb.Add($"orden={Uri.EscapeDataString(parameters.Orden)}");
        if (parameters.Stock.HasValue) qb.Add($"stock={(parameters.Stock.Value ? 1 : 0)}");
        if (parameters.Agrupar.HasValue) qb.Add($"agrupar={(parameters.Agrupar.Value ? 1 : 0)}");
        if (parameters.Pagina.HasValue) qb.Add($"pagina={parameters.Pagina.Value}");
        if (qb.Count > 0) url += "?" + string.Join("&", qb);

        _logger?.LogDebug("GET {Url}", url);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Response {StatusCode} for GET {Url}", (int)resp.StatusCode, url);
        await EnsureSuccess(resp, cancellationToken).ConfigureAwait(false);
        return await ReadAs<SyscomProductsSearchResponse>(resp, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SyscomProduct> GetProductInfoAsync(SyscomProductInfoRequest parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrWhiteSpace(parameters.Id))
        {
            throw new SyscomClientException($" Value cannot be null or whitespace: {nameof(parameters.Id)}");
        }

        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
        var url = Combine(_options.ApiBasePath, $"/productos/{Uri.EscapeDataString(parameters.Id)}");
        _logger?.LogDebug("GET {Url}", url);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Response {StatusCode} for GET {Url}", (int)resp.StatusCode, url);
        await EnsureSuccess(resp, cancellationToken).ConfigureAwait(false);
        return await ReadAs<SyscomProduct>(resp, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JsonDocument> GetRelatedProductsAsync(SyscomRelatedProductsRequest parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrWhiteSpace(parameters.Id))
        {
            throw new SyscomClientException($" Value cannot be null or whitespace: {nameof(parameters.Id)}");
        }

        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
        var url = Combine(_options.ApiBasePath, $"/productos/{Uri.EscapeDataString(parameters.Id)}/relacionados");
        _logger?.LogDebug("GET {Url}", url);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Response {StatusCode} for GET {Url}", (int)resp.StatusCode, url);
        await EnsureSuccess(resp, cancellationToken).ConfigureAwait(false);
        return await ReadJson(resp, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JsonDocument> GetProductAccesoriesAsync(SyscomProductsAccesoriesRequest parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrWhiteSpace(parameters.Id))
        {
            throw new SyscomClientException($" Value cannot be null or whitespace: {nameof(parameters.Id)}");
        }

        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
        var url = Combine(_options.ApiBasePath, $"/productos/{Uri.EscapeDataString(parameters.Id)}/accesorios");
        _logger?.LogDebug("GET {Url}", url);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Response {StatusCode} for GET {Url}", (int)resp.StatusCode, url);
        await EnsureSuccess(resp, cancellationToken).ConfigureAwait(false);
        return await ReadJson(resp, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        var auth = _http.DefaultRequestHeaders.Authorization;
        if (auth != null && string.Equals(auth.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(auth.Parameter))
        {
            _logger?.LogTrace("Already authenticated via existing Authorization header.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
            _logger?.LogDebug("Applied Authorization header from options.AccessToken.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(_options.ClientId) && !string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            _logger?.LogInformation(
                "No token present; attempting to obtain access token using configured ClientId/ClientSecret.");
            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(token.AccessToken))
            {
                _logger?.LogInformation("Access token obtained automatically.");
                return;
            }

            _logger?.LogError("Failed to obtain access token from SYSCOM API.");
            throw new SyscomClientException("Failed to obtain access token from SYSCOM API.");
        }

        _logger?.LogError("SyscomClient is not authenticated and no credentials were provided.");
        throw new SyscomClientException(
            "SyscomClient is not authenticated. Provide AccessToken in options or ClientId/ClientSecret to auto-fetch."
        );
    }

    // Helpers
    private async Task EnsureSuccess(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;

        string? body = null;
        try
        {
            body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Syscom API request failed with {StatusCode} {Reason}. Body: {Body}",
                (int)resp.StatusCode,
                resp.ReasonPhrase, Truncate(body, 4096));
        }

        throw new SyscomClientException(
            $"Request failed with {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}");
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage resp, CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    private static async Task<T> ReadAs<T>(HttpResponseMessage resp, CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct).ConfigureAwait(false);
        if (value == null)
        {
            throw new SyscomClientException($"Failed to deserialize response to {typeof(T).Name}");
        }

        return value;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (value == null) return null;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    private static string Combine(string left, string right)
    {
        if (string.IsNullOrEmpty(left)) return right;
        if (string.IsNullOrEmpty(right)) return left;
        if (left.EndsWith('/')) left = left.TrimEnd('/');
        return left + (right.StartsWith('/') ? right : "/" + right);
    }
}