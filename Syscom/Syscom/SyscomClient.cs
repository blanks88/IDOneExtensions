using System.Net;
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
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private sealed record TokenState(string Value, DateTimeOffset? ExpiresAt);
    private volatile TokenState? _token;

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
        if (options.RequestTimeout <= TimeSpan.Zero || options.MaxGetDuration <= TimeSpan.Zero ||
            options.MaxGetAttempts < 1 || options.GetRetryBaseDelay < TimeSpan.Zero ||
            options.GetRetryMaxDelay < options.GetRetryBaseDelay || options.TokenRefreshSkew < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid SYSCOM HTTP resilience options.");

        if (_http.BaseAddress == null && !string.IsNullOrWhiteSpace(options.BaseUrl))
            _http.BaseAddress = new Uri(options.BaseUrl);

        var initialToken = !string.IsNullOrWhiteSpace(options.AccessToken)
            ? options.AccessToken
            : _http.DefaultRequestHeaders.Authorization is { Scheme: "Bearer" } header
                ? header.Parameter
                : null;
        if (!string.IsNullOrWhiteSpace(initialToken)) _token = new TokenState(initialToken, null);
    }

    public async Task<SyscomAccessTokenResponse> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await RequestAccessTokenAsync(cancellationToken).ConfigureAwait(false); }
        finally { _tokenLock.Release(); }
    }

    public Task<List<SyscomCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        GetAsync(Combine(_options.ApiBasePath, "/categorias"), ReadAs<List<SyscomCategory>>, cancellationToken);

    public Task<SyscomCategory> GetCategoriyByIdAsync(int id, CancellationToken cancellationToken = default) =>
        GetAsync(Combine(_options.ApiBasePath, $"/categorias/{id}"), ReadAs<SyscomCategory>, cancellationToken);

    public Task<JsonDocument> GetBillsAsync(SyscomBillsRequest parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var url = Combine(_options.ApiBasePath, "/facturas");
        var qb = new List<string>();
        if (parameters.Anio.HasValue) qb.Add($"anio={parameters.Anio.Value}");
        if (!string.IsNullOrWhiteSpace(parameters.Busqueda)) qb.Add($"busqueda={Uri.EscapeDataString(parameters.Busqueda)}");
        if (parameters.Pagina.HasValue) qb.Add($"pagina={parameters.Pagina.Value}");
        return GetAsync(AddQuery(url, qb), ReadJson, cancellationToken);
    }

    public Task<JsonDocument> GetBillDetailAsync(SyscomFacturaDetalleRequest parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrWhiteSpace(parameters.Id)) throw new SyscomClientException("Bill ID is required.");
        var url = Combine(_options.ApiBasePath, "/facturas") + $"?id={Uri.EscapeDataString(parameters.Id)}";
        return GetAsync(url, ReadJson, cancellationToken);
    }

    public Task<SyscomProductsSearchResponse> GetProductsAsync(SyscomProductsRequest parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Limit is < 10 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(parameters), "Product limit must be between 10 and 1000.");
        var url = Combine(_options.ApiBasePath, "/productos");
        var qb = new List<string>();
        if (parameters.Categoria.HasValue) qb.Add($"categoria={parameters.Categoria.Value}");
        if (!string.IsNullOrWhiteSpace(parameters.Marca)) qb.Add($"marca={Uri.EscapeDataString(parameters.Marca)}");
        if (!string.IsNullOrWhiteSpace(parameters.Busqueda)) qb.Add($"busqueda={Uri.EscapeDataString(parameters.Busqueda)}");
        if (!string.IsNullOrWhiteSpace(parameters.Sucursal)) qb.Add($"sucursal={Uri.EscapeDataString(parameters.Sucursal)}");
        if (!string.IsNullOrWhiteSpace(parameters.Orden)) qb.Add($"orden={Uri.EscapeDataString(parameters.Orden)}");
        if (parameters.Stock.HasValue) qb.Add($"stock={(parameters.Stock.Value ? 1 : 0)}");
        if (parameters.Agrupar.HasValue) qb.Add($"agrupar={(parameters.Agrupar.Value ? 1 : 0)}");
        if (parameters.Pagina.HasValue) qb.Add($"pagina={parameters.Pagina.Value}");
        if (parameters.Limit.HasValue) qb.Add($"limit={parameters.Limit.Value}");
        return GetAsync(AddQuery(url, qb), ReadAs<SyscomProductsSearchResponse>, cancellationToken);
    }

    public Task<SyscomProduct> GetProductInfoAsync(SyscomProductInfoRequest parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrWhiteSpace(parameters.Id)) throw new SyscomClientException("Product ID is required.");
        return GetAsync(Combine(_options.ApiBasePath, $"/productos/{Uri.EscapeDataString(parameters.Id)}"),
            ReadAs<SyscomProduct>, cancellationToken);
    }

    public Task<JsonDocument> GetRelatedProductsAsync(SyscomRelatedProductsRequest parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrWhiteSpace(parameters.Id)) throw new SyscomClientException("Product ID is required.");
        return GetAsync(Combine(_options.ApiBasePath, $"/productos/{Uri.EscapeDataString(parameters.Id)}/relacionados"),
            ReadJson, cancellationToken);
    }

    public Task<JsonDocument> GetProductAccesoriesAsync(SyscomProductsAccesoriesRequest parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrWhiteSpace(parameters.Id)) throw new SyscomClientException("Product ID is required.");
        return GetAsync(Combine(_options.ApiBasePath, $"/productos/{Uri.EscapeDataString(parameters.Id)}/accesorios"),
            ReadJson, cancellationToken);
    }

    private async Task<SyscomAccessTokenResponse> RequestAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new SyscomClientException("SYSCOM client credentials are required to renew the access token.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        using var req = new HttpRequestMessage(HttpMethod.Post, _options.OAuthTokenPath)
        {
            Content = _options.UseFormEncodedTokenRequest
                ? new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _options.ClientId,
                    ["client_secret"] = _options.ClientSecret,
                    ["grant_type"] = "client_credentials"
                })
                : new StringContent(JsonSerializer.Serialize(new
                {
                    client_id = _options.ClientId,
                    client_secret = _options.ClientSecret,
                    grant_type = "client_credentials"
                }), Encoding.UTF8, "application/json")
        };
        try
        {
            using var response = await _http.SendAsync(req, timeout.Token).ConfigureAwait(false);
            EnsureSuccess(response);
            var token = await ReadAs<SyscomAccessTokenResponse>(response, timeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token.AccessToken))
                throw new SyscomClientException("SYSCOM token response did not contain an access token.");
            DateTimeOffset? expiresAt = token.ExpiresIn is > 0
                ? DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn.Value)
                : null;
            _token = new TokenState(token.AccessToken, expiresAt);
            _options.AccessToken = token.AccessToken;
            _logger?.LogInformation("SYSCOM access token renewed.");
            return token;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SyscomClientException("SYSCOM token request timed out.");
        }
        catch (HttpRequestException)
        {
            throw new SyscomClientException("SYSCOM token request failed due to a network error.");
        }
        catch (JsonException)
        {
            throw new SyscomClientException("SYSCOM token response was invalid JSON.");
        }
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (IsCurrent(_token))
            return;
        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsCurrent(_token))
                return;
            await RequestAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _tokenLock.Release(); }
    }

    private async Task<bool> RenewAfterUnauthorizedAsync(string? usedToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret)) return false;
        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (string.Equals(_token?.Value, usedToken, StringComparison.Ordinal))
                await RequestAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _tokenLock.Release(); }
    }

    private async Task<T> GetAsync<T>(string url, Func<HttpResponseMessage, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        total.CancelAfter(_options.MaxGetDuration);
        var transientAttempts = 0;
        var renewedAfter401 = false;
        while (true)
        {
            if (total.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new SyscomClientException("SYSCOM GET duration limit exceeded.");
            }
            try { await EnsureAuthenticatedAsync(total.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new SyscomClientException("SYSCOM GET duration limit exceeded."); }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
            timeout.CancelAfter(_options.RequestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var usedToken = _token?.Value;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", usedToken);
            HttpResponseMessage? response = null;
            try
            {
                response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Unauthorized && !renewedAfter401)
                {
                    renewedAfter401 = true;
                    if (await RenewAfterUnauthorizedAsync(usedToken, total.Token).ConfigureAwait(false)) continue;
                }
                if (response.IsSuccessStatusCode)
                {
                    try { return await read(response, timeout.Token).ConfigureAwait(false); }
                    catch (JsonException) { throw new SyscomClientException("SYSCOM GET response was invalid JSON."); }
                }
                if (!IsTransient(response.StatusCode) || ++transientAttempts >= _options.MaxGetAttempts)
                    throw Failure(response.StatusCode);
                await DelayAsync(response.Headers.RetryAfter, transientAttempts, total.Token).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                if (++transientAttempts >= _options.MaxGetAttempts)
                    throw new SyscomClientException("SYSCOM GET failed after network retries.");
                await DelayAsync(null, transientAttempts, total.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (total.IsCancellationRequested)
                    throw new SyscomClientException("SYSCOM GET duration limit exceeded.");
                if (++transientAttempts >= _options.MaxGetAttempts)
                    throw new SyscomClientException("SYSCOM GET timed out after retries.");
                await DelayAsync(null, transientAttempts, total.Token).ConfigureAwait(false);
            }
            finally { response?.Dispose(); }
        }
    }

    private async Task DelayAsync(RetryConditionHeaderValue? retryAfter, int attempt, CancellationToken ct)
    {
        var backoffMs = Math.Min(_options.GetRetryMaxDelay.TotalMilliseconds,
            _options.GetRetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        var jitterMs = backoffMs * (0.5 + Random.Shared.NextDouble() * 0.5);
        var serverDelay = retryAfter?.Delta ??
            (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.Zero);
        // Retry-After is a server minimum. If it exceeds the total GET budget,
        // the linked cancellation stops the wait instead of sending early.
        var delay = TimeSpan.FromMilliseconds(Math.Min(_options.MaxGetDuration.TotalMilliseconds,
            Math.Max(jitterMs, Math.Max(0, serverDelay.TotalMilliseconds))));
        _logger?.LogWarning("Retrying SYSCOM GET after transient failure; attempt {Attempt}.", attempt + 1);
        await Task.Delay(delay, ct).ConfigureAwait(false);
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.RequestTimeout || status == (HttpStatusCode)429 || (int)status >= 500;

    private bool IsCurrent(TokenState? token) => token is not null &&
        (!token.ExpiresAt.HasValue || DateTimeOffset.UtcNow + _options.TokenRefreshSkew < token.ExpiresAt.Value);

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) throw Failure(response.StatusCode);
    }

    private static SyscomClientException Failure(HttpStatusCode status) =>
        new($"SYSCOM request failed with HTTP {(int)status}.");

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    private static async Task<T> ReadAs<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct).ConfigureAwait(false);
        return value ?? throw new SyscomClientException("SYSCOM response was empty.");
    }

    private static string AddQuery(string url, List<string> query) =>
        query.Count == 0 ? url : url + "?" + string.Join("&", query);

    private static string Combine(string left, string right)
    {
        if (string.IsNullOrEmpty(left)) return right;
        if (string.IsNullOrEmpty(right)) return left;
        return left.TrimEnd('/') + (right.StartsWith('/') ? right : "/" + right);
    }
}
