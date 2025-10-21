using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Synchroteam.Infrastructure;

internal sealed class SynchroteamHttpClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly SynchroteamClientOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    public SynchroteamHttpClient(SynchroteamClientOptions options, HttpMessageHandler? handler = null)
    {
        _options = options;
        var httpHandler = handler ?? CreateDefaultHandler(options);
        _http = new HttpClient(httpHandler) { Timeout = options.Timeout };
        _http.BaseAddress = CombineBaseAddress(options.BaseAddress, options.ApiBasePath);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };
    }

    public void Dispose() => _http.Dispose();

    public async Task<T?> GetAsync<T>(string path, Dictionary<string, string?>? query = null,
        CancellationToken ct = default)
    {
        var uri = AppendQuery(new Uri(_http.BaseAddress!, path), query);
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        AddDefaultHeaders(req);
        using var resp = await SendWithRetryAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp).ConfigureAwait(false);
        if (resp.Content.Headers.ContentLength == 0) return default;
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, ct).ConfigureAwait(false);
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest payload,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8,
            "application/json");
        AddDefaultHeaders(req);
        using var resp = await SendWithRetryAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp).ConfigureAwait(false);
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<TResponse>(stream, _jsonOptions, ct).ConfigureAwait(false);
    }

    public async Task<TResponse?> PutAsync<TRequest, TResponse>(string path, TRequest payload,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8,
                "application/json")
        };
        AddDefaultHeaders(req);
        using var resp = await SendWithRetryAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp).ConfigureAwait(false);
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<TResponse>(stream, _jsonOptions, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, path);
        AddDefaultHeaders(req);
        using var resp = await SendWithRetryAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var retries = 0;
        var delay = _options.InitialBackoff;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(Clone(req), HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested && retries < _options.MaxRetries)
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
                retries++;
                delay = NextDelay(delay);
                continue;
            }

            if (IsTransient(resp.StatusCode) && retries < _options.MaxRetries)
            {
                // Honor Retry-After if present
                var retryAfter = GetRetryAfter(resp);
                var wait = retryAfter ?? delay;
                if (wait > _options.MaxBackoff) wait = _options.MaxBackoff;
                await Task.Delay(wait, ct).ConfigureAwait(false);
                retries++;
                delay = NextDelay(delay);
                continue;
            }

            return resp;
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout
           || statusCode == (HttpStatusCode)429
           || (int)statusCode >= 500;

    private static TimeSpan? GetRetryAfter(HttpResponseMessage resp)
    {
        if (resp.Headers.RetryAfter?.Delta is { } delta)
            return delta;
        if (resp.Headers.RetryAfter?.Date is { } date)
            return date - DateTimeOffset.UtcNow;
        return null;
    }

    private TimeSpan NextDelay(TimeSpan current)
    {
        var next = TimeSpan.FromMilliseconds(current.TotalMilliseconds * 2);
        return next > _options.MaxBackoff ? _options.MaxBackoff : next;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = resp.Content != null ? await resp.Content.ReadAsStringAsync().ConfigureAwait(false) : string.Empty;
        if (_options.ThrowOnErrorStatus)
        {
            var requestId = resp.Headers.TryGetValues("x-request-id", out var vals) ? vals.FirstOrDefault() : null;
            throw SynchroteamApiException.FromResponse(resp.StatusCode, body, requestId);
        }
    }

    private void AddDefaultHeaders(HttpRequestMessage req)
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            req.Headers.TryAddWithoutValidation(_options.ApiKeyHeaderName, _options.ApiKey);
        }

        foreach (var kvp in _options.DefaultHeaders)
        {
            if (req.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value))
            {
                continue;
            }

            // If it doesn't fit header rules, send as content header when content exists
            req.Content?.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
        }

        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static HttpMessageHandler CreateDefaultHandler(SynchroteamClientOptions options)
    {
        var handler = new HttpClientHandler();
        if (options.Proxy == null)
        {
            return handler;
        }

        handler.Proxy = options.Proxy;
        handler.UseProxy = true;

        return handler;
    }

    private static Uri CombineBaseAddress(Uri baseAddress, string apiBasePath)
    {
        var baseUri = baseAddress.ToString().TrimEnd('/') + "/" + apiBasePath.Trim('/');
        if (!baseUri.EndsWith('/')) baseUri += "/";
        return new Uri(baseUri, UriKind.Absolute);
    }

    private static Uri AppendQuery(Uri baseUri, Dictionary<string, string?>? query)
    {
        if (query == null || query.Count == 0) return baseUri;
        var sb = new StringBuilder(baseUri.ToString());
        sb.Append(baseUri.Query.Length == 0 ? '?' : '&');
        var first = true;
        foreach (var kv in query)
        {
            if (!first) sb.Append('&');
            first = false;
            sb.Append(Uri.EscapeDataString(kv.Key));
            sb.Append('=');
            if (kv.Value != null)
                sb.Append(Uri.EscapeDataString(kv.Value));
        }

        return new Uri(sb.ToString(), UriKind.Absolute);
    }

    private static HttpRequestMessage Clone(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        // Copy headers
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        // Copy content
        if (request.Content == null)
        {
            return clone;
        }

        var ms = new MemoryStream();
        request.Content.CopyTo(ms, null, default);
        ms.Position = 0;
        clone.Content = new StreamContent(ms);
        foreach (var h in request.Content.Headers)
            clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);

        return clone;
    }
}