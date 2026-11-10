using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Syscom;

namespace SyscomTestProject;

public class SyscomHttpResilienceTests
{
    private const string Secret = "secret-sentinel-do-not-log";
    private const string BodySecret = "body-sentinel-do-not-log";

    [Fact]
    public async Task ExpiringTokenIsRenewedBeforeNextGet()
    {
        var tokenCalls = 0;
        var getTokens = new List<string?>();
        var logger = new CaptureLogger();
        var client = Create((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
                return Task.FromResult(Json(HttpStatusCode.OK,
                    $"{{\"access_token\":\"token-{++tokenCalls}\",\"expires_in\":1}}"));
            getTokens.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(Json(HttpStatusCode.OK, "[]"));
        }, Options() with { TokenRefreshSkew = TimeSpan.FromSeconds(2) }, logger);

        await client.GetCategoriesAsync();
        await client.GetCategoriesAsync();

        Assert.Equal(2, tokenCalls);
        Assert.Equal(new[] { "token-1", "token-2" }, getTokens);
        Assert.DoesNotContain(Secret, logger.Messages);
        Assert.DoesNotContain("token-1", logger.Messages);
        Assert.DoesNotContain("token-2", logger.Messages);
    }

    [Fact]
    public async Task UnauthorizedRenewsAndReplaysOnlyOnce()
    {
        var postCalls = 0;
        var getCalls = 0;
        var client = Create((request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.OK, $"{{\"access_token\":\"token-{++postCalls}\",\"expires_in\":3600}}")
            : Json(HttpStatusCode.Unauthorized, BodySecret + ++getCalls)), Options());

        var error = await Assert.ThrowsAsync<SyscomClientException>(() => client.GetCategoriesAsync());

        Assert.Equal(2, postCalls);
        Assert.Equal(2, getCalls);
        Assert.Contains("401", error.Message);
        Assert.DoesNotContain(BodySecret, error.ToString());
    }

    [Fact]
    public async Task UnauthorizedRefreshCanRecover()
    {
        var postCalls = 0;
        var getTokens = new List<string?>();
        var client = Create((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
                return Task.FromResult(Json(HttpStatusCode.OK,
                    $"{{\"access_token\":\"token-{++postCalls}\",\"expires_in\":3600}}"));
            getTokens.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(Json(getTokens.Count == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK,
                getTokens.Count == 1 ? BodySecret : "[]"));
        }, Options());

        Assert.Empty(await client.GetCategoriesAsync());
        Assert.Equal(2, postCalls);
        Assert.Equal(new[] { "token-1", "token-2" }, getTokens);
    }

    [Fact]
    public async Task TransientStatusesRetryWithFiniteAttemptsAndRetryAfter()
    {
        var calls = 0;
        var client = Create((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var count = ++calls;
            if (count == 1)
            {
                var response = Json((HttpStatusCode)429, BodySecret);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(response);
            }
            return Task.FromResult(count == 2
                ? Json(HttpStatusCode.ServiceUnavailable, BodySecret)
                : Json(HttpStatusCode.OK, "[]"));
        }, Options() with { AccessToken = "provided", GetRetryBaseDelay = TimeSpan.Zero, GetRetryMaxDelay = TimeSpan.Zero });

        Assert.Empty(await client.GetCategoriesAsync());
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task PermanentFailureDoesNotRetryOrExposeResponseBody()
    {
        var calls = 0;
        var logger = new CaptureLogger();
        var client = Create((_, _) =>
        {
            calls++;
            return Task.FromResult(Json(HttpStatusCode.BadRequest, BodySecret));
        }, Options() with { AccessToken = "provided" }, logger);

        var error = await Assert.ThrowsAsync<SyscomClientException>(() => client.GetCategoriesAsync());

        Assert.Equal(1, calls);
        Assert.DoesNotContain(BodySecret, error.ToString());
        Assert.DoesNotContain(Secret, error.ToString());
        Assert.DoesNotContain(BodySecret, logger.Messages);
        Assert.DoesNotContain(Secret, logger.Messages);
    }

    [Fact]
    public async Task NetworkFailureRetriesOnlyConfiguredNumberOfTimes()
    {
        var calls = 0;
        var client = Create((_, _) =>
        {
            calls++;
            throw new HttpRequestException(Secret);
        }, Options() with { AccessToken = "provided", MaxGetAttempts = 2,
            GetRetryBaseDelay = TimeSpan.Zero, GetRetryMaxDelay = TimeSpan.Zero });

        var error = await Assert.ThrowsAsync<SyscomClientException>(() => client.GetCategoriesAsync());

        Assert.Equal(2, calls);
        Assert.DoesNotContain(Secret, error.ToString());
    }

    [Fact]
    public async Task RequestTimeoutRetriesAndCallerCancellationStopsImmediately()
    {
        var calls = 0;
        var client = Create(async (_, ct) =>
        {
            calls++;
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json(HttpStatusCode.OK, "[]");
        }, Options() with { AccessToken = "provided", MaxGetAttempts = 2,
            RequestTimeout = TimeSpan.FromMilliseconds(20), MaxGetDuration = TimeSpan.FromSeconds(1),
            GetRetryBaseDelay = TimeSpan.Zero, GetRetryMaxDelay = TimeSpan.Zero });

        var error = await Assert.ThrowsAsync<SyscomClientException>(() => client.GetCategoriesAsync());
        Assert.Contains("timed out", error.Message);
        Assert.Equal(2, calls);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetCategoriesAsync(cancellation.Token));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RetryAfterIsCappedByGetDuration()
    {
        var calls = 0;
        var client = Create((_, _) =>
        {
            calls++;
            var response = Json((HttpStatusCode)429, BodySecret);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            return Task.FromResult(response);
        }, Options() with { AccessToken = "provided", MaxGetDuration = TimeSpan.FromMilliseconds(50),
            GetRetryBaseDelay = TimeSpan.Zero, GetRetryMaxDelay = TimeSpan.FromMilliseconds(5) });

        var error = await Assert.ThrowsAsync<SyscomClientException>(() => client.GetCategoriesAsync());
        Assert.Contains("duration limit", error.Message);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task InvalidJsonNeverExposesResponseContent()
    {
        var client = Create((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{" + BodySecret)),
            Options() with { AccessToken = "provided" });

        var error = await Assert.ThrowsAsync<SyscomClientException>(() => client.GetCategoriesAsync());
        Assert.DoesNotContain(BodySecret, error.ToString());
    }

    [Theory]
    [InlineData(false, "/oauth/token", "application/json")]
    [InlineData(true, "/api/v1/oauth/token", "application/x-www-form-urlencoded")]
    public async Task TokenPostIsNeverRetriedAndUsesConfiguredEncoding(bool form, string path, string contentType)
    {
        var calls = 0;
        var client = Create((request, _) =>
        {
            calls++;
            Assert.Equal(path, request.RequestUri?.AbsolutePath);
            Assert.Equal(contentType, request.Content?.Headers.ContentType?.MediaType);
            return Task.FromResult(Json(HttpStatusCode.ServiceUnavailable, BodySecret));
        }, Options() with { OAuthTokenPath = path, UseFormEncodedTokenRequest = form });

        var error = await Assert.ThrowsAsync<SyscomClientException>(() => client.GetAccessTokenAsync());
        Assert.Equal(1, calls);
        Assert.DoesNotContain(BodySecret, error.ToString());
    }

    private static SyscomClientOptions Options() => new("id-sentinel", Secret)
    {
        BaseUrl = "https://syscom.test",
        GetRetryBaseDelay = TimeSpan.FromMilliseconds(1),
        GetRetryMaxDelay = TimeSpan.FromMilliseconds(1),
        MaxGetDuration = TimeSpan.FromSeconds(2)
    };

    private static SyscomClient Create(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        SyscomClientOptions options, ILogger<SyscomClient>? logger = null) =>
        new(new HttpClient(new StubHandler(send)), options, logger);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class CaptureLogger : ILogger<SyscomClient>
    {
        public string Messages { get; private set; } = string.Empty;
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages += formatter(state, exception);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
