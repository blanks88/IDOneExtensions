using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Utils.Extensions;

namespace Synchroteam.Infrastructure.V3;

internal sealed class SynchroteamHttpClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly SynchroteamClientOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string ApiVersion = "api/v3";
    
    public SynchroteamHttpClient(SynchroteamClientOptions options, HttpMessageHandler? handler = null)
    {
        _options = options;
        var httpHandler = handler ?? CreateDefaultHandler(options);
        _http = new HttpClient(httpHandler)
        {
            Timeout = options.Timeout,
            BaseAddress = options.BaseUrl
        };

        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Domain}:{options.ApiKey}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);

        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };
        return;
        
        static HttpMessageHandler CreateDefaultHandler(SynchroteamClientOptions options)
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
    }

    public async Task<T?> GetAsync<T>(string path, Dictionary<string, string?>? query = null,
        CancellationToken ct = default)
    {
        var uri = AppendQuery(new Uri(_http.BaseAddress!, ApiVersion.ConcatToPath(path)), query);
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        using var resp = await SendWithRetryAsync(req, ct).ConfigureAwait(false);

        await EnsureSuccessAsync(resp).ConfigureAwait(false);
        if (resp.Content.Headers.ContentLength == 0)
        {
            return default;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, ct).ConfigureAwait(false);
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest payload,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, ApiVersion.ConcatToPath(path));
        req.Content = new StringContent(
            JsonSerializer.Serialize(payload, _jsonOptions),
            Encoding.UTF8, "application/json"
        );

        using var resp = await SendWithRetryAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp).ConfigureAwait(false);

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<TResponse>(stream, _jsonOptions, ct).ConfigureAwait(false);
    }

    public async Task<TResponse?> PutAsync<TRequest, TResponse>(string path, TRequest payload,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, ApiVersion.ConcatToPath(path));
        req.Content = new StringContent(
            JsonSerializer.Serialize(payload, _jsonOptions),
            Encoding.UTF8, "application/json"
        );

        using var resp = await SendWithRetryAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp).ConfigureAwait(false);

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<TResponse>(stream, _jsonOptions, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, ApiVersion.ConcatToPath(path));
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
                resp = await _http.SendAsync(Clone(req, ct), HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested && retries < _options.MaxRetries)
            {
                LogWarn($"Request timed out: {req.Method} {req.RequestUri}. Retry {retries + 1}/{_options.MaxRetries}. Waiting {delay}.");
                await Task.Delay(delay, ct).ConfigureAwait(false);
                retries++;
                delay = NextDelay(delay);
                continue;
            }

            // Parse rate limit/quota headers
            var rl = ParseRateLimit(resp);
            if (rl != null)
            {
                LogInfo($"RateLimit: limit={rl.Limit}, remaining={rl.Remaining}, reset={rl.ResetUnixUtc}, quotaLimit={rl.QuotaLimit}, quotaRemaining={rl.QuotaRemaining}");
            }

            // If daily quota exhausted, do not retry – it won't help until midnight UTC
            if (rl?.QuotaRemaining == 0)
            {
                LogError("Daily quota exhausted (X-Quota-Remaining=0). Not retrying until next UTC midnight.");
                return resp;
            }

            if (!IsTransient(resp.StatusCode) || retries >= _options.MaxRetries)
            {
                return resp;
            }

            // Prefer waiting until X-RateLimit-Reset if remaining is 0 or status 429
            TimeSpan? wait = null;
            if (rl is { Remaining: 0, ResetUnixUtc: { } resetTs })
            {
                wait = SecondsUntil(resetTs);
            }
            if (resp.StatusCode == (HttpStatusCode)429 && rl?.ResetUnixUtc is { } resetTs429)
            {
                var tmp = SecondsUntil(resetTs429);
                wait = Max(wait, tmp);
            }

            // Honor Retry-After header if present
            var retryAfter = GetRetryAfter(resp);
            wait = Max(wait, retryAfter);

            // Fallback to exponential backoff
            if (wait is null)
            {
                wait = delay;
            }

            if (wait > _options.MaxBackoff)
            {
                wait = _options.MaxBackoff;
            }

            LogWarn($"Transient {(int)resp.StatusCode} {resp.ReasonPhrase}. Retry {retries + 1}/{_options.MaxRetries} after {wait}.");
            await Task.Delay(wait.Value, ct).ConfigureAwait(false);
            
            retries++;
            delay = NextDelay(delay);
        }
        
        // Local functions
        static bool IsTransient(HttpStatusCode statusCode)
            => statusCode == HttpStatusCode.RequestTimeout
               || statusCode == (HttpStatusCode)429
               || (int)statusCode >= 500;

        static TimeSpan? GetRetryAfter(HttpResponseMessage resp)
        {
            if (resp.Headers.RetryAfter?.Delta is { } delta)
            {
                return delta;
            }

            if (resp.Headers.RetryAfter?.Date is { } date)
            {
                return date - DateTimeOffset.UtcNow;
            }

            return null;
        }

        static TimeSpan? Max(TimeSpan? a, TimeSpan? b)
        {
            if (a is null) return b;
            if (b is null) return a;
            return a.Value > b.Value ? a : b;
        }

        static TimeSpan SecondsUntil(long unixTs)
        {
            var resetAt = DateTimeOffset.FromUnixTimeSeconds(unixTs);
            var now = DateTimeOffset.UtcNow;
            var span = resetAt - now;
            return span < TimeSpan.Zero ? TimeSpan.Zero : span;
        }

        TimeSpan NextDelay(TimeSpan current)
        {
            var next = TimeSpan.FromMilliseconds(current.TotalMilliseconds * 2);
            return next > _options.MaxBackoff ? _options.MaxBackoff : next;
        }

        static RateLimitHeaders? ParseRateLimit(HttpResponseMessage resp)
        {
            var h = resp.Headers;
            var x = new RateLimitHeaders();
            if (h.TryGetValues("X-RateLimit-Limit", out var v1) && long.TryParse(v1.FirstOrDefault(), out var limit)) x.Limit = limit;
            if (h.TryGetValues("X-RateLimit-Remaining", out var v2) && long.TryParse(v2.FirstOrDefault(), out var rem)) x.Remaining = rem;
            if (h.TryGetValues("X-RateLimit-Reset", out var v3) && long.TryParse(v3.FirstOrDefault(), out var reset)) x.ResetUnixUtc = reset;
            if (h.TryGetValues("X-Quota-Limit", out var v4) && long.TryParse(v4.FirstOrDefault(), out var ql)) x.QuotaLimit = ql;
            if (h.TryGetValues("X-Quota-Remaining", out var v5) && long.TryParse(v5.FirstOrDefault(), out var qr)) x.QuotaRemaining = qr;
            return x.IsEmpty ? null : x;
        }
    }

    private sealed class RateLimitHeaders
    {
        public long? Limit { get; set; }
        public long? Remaining { get; set; }
        public long? ResetUnixUtc { get; set; }
        public long? QuotaLimit { get; set; }
        public long? QuotaRemaining { get; set; }
        public bool IsEmpty => Limit is null && Remaining is null && ResetUnixUtc is null && QuotaLimit is null && QuotaRemaining is null;
    }

    private static void LogInfo(string message) => Console.Error.WriteLine($"[INFO] {DateTimeOffset.UtcNow:o} {message}");
    private static void LogWarn(string message) => Console.Error.WriteLine($"[WARN] {DateTimeOffset.UtcNow:o} {message}");
    private static void LogError(string message) => Console.Error.WriteLine($"[ERROR] {DateTimeOffset.UtcNow:o} {message}");

    private async Task EnsureSuccessAsync(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

        var code = (int)resp.StatusCode;
        var msg = code switch
        {
            400 => "Bad Request -- You have an error in your request",
            401 => "Unauthorized -- Your API key is wrong",
            403 => "Forbidden -- The resource you are trying to access is Forbidden",
            404 => "Not Found -- The requested resource does not exist",
            405 => "Method Not Allowed -- You tried to access an source with an invalid method",
            406 => "Not Acceptable -- You requested a format that isn't JSON",
            429 => "Too Many Requests -- You've hit our API rate limits",
            500 => "Internal Server Error -- We had a problem with our server. Try again later.",
            503 => "Service Unavailable -- We're temporarily offline for maintenance. Please try again later.",
            _ => $"HTTP error {(int)resp.StatusCode} ({resp.ReasonPhrase})"
        };

        if (code is >= 500 or 429)
        {
            LogError($"{msg}. Status={(int)resp.StatusCode}. Body length={body?.Length ?? 0}.");
        }
        else
        {
            LogWarn($"{msg}. Status={(int)resp.StatusCode}. Body length={body?.Length ?? 0}.");
        }

        if (_options.ThrowOnErrorStatus)
        {
            var requestId = resp.Headers.TryGetValues("x-request-id", out var vals) ? vals.FirstOrDefault() : null;
            throw SynchroteamApiException.FromResponse(resp.StatusCode, body, requestId);
        }
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

    private static HttpRequestMessage Clone(HttpRequestMessage request, CancellationToken ct = default)
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
        request.Content.CopyTo(ms, null, ct);
        ms.Position = 0;
        clone.Content = new StreamContent(ms);
        foreach (var h in request.Content.Headers)
        {
            clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        return clone;
    }

    public void Dispose() => _http.Dispose();
}