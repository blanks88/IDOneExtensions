using System.Net;

namespace Synchroteam.Infrastructure.V3;

public sealed class SynchroteamClientOptions
{
    /// <summary>
    /// Base URL of the Synchroteam.Infrastructure tenant, e.g. https://yourtenant.synchroteam.com
    /// The client will append /api/v3 or the value provided in <see cref="ApiBasePath"/>.
    /// </summary>
    public required Uri BaseUrl { get; init; }
    
    /// <summary>
    /// API user value. If null, no Authorization header will be sent.
    /// </summary>
    public string? Domain { get; init; }
    
    /// <summary>
    /// API key/token value. If null, no Authorization header will be sent.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Header name for API key. Defaults to "X-Api-Key".
    /// </summary>
    public string ApiKeyHeaderName { get; init; } = "X-Api-Key";

    /// <summary>
    /// Optional static headers to be added on every request (e.g., tenant, username).
    /// </summary>
    public Dictionary<string, string> DefaultHeaders { get; init; } = new();

    /// <summary>
    /// Request timeout. Default 30 seconds.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum automatic retries for transient errors (timeouts, 429, 5xx). Default 3.
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Initial backoff for retries. Default 500ms.
    /// </summary>
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Maximum backoff. Default 8 seconds.
    /// </summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// When true, throws on non-success HTTP status codes using SynchroteamApiException.
    /// </summary>
    public bool ThrowOnErrorStatus { get; init; } = true;

    /// <summary>
    /// Optional HTTP proxy.
    /// </summary>
    public IWebProxy? Proxy { get; init; }
}
