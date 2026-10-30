namespace Syscom;

public record SyscomClientOptions(string ClientId, string ClientSecret)
{
    // Base API URL, default to developers.syscom.mx
    public string BaseUrl { get; init; } = "https://developers.syscom.mx";

    // OAuth endpoint relative path
    public string OAuthTokenPath { get; init; } = "/oauth/token";
    public bool UseFormEncodedTokenRequest { get; init; }

    // API base path
    public string ApiBasePath { get; } = "/api/v1";

    // OAuth access token
    public string? AccessToken { get; set; }

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxGetAttempts { get; init; } = 4;
    public TimeSpan GetRetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan GetRetryMaxDelay { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan MaxGetDuration { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan TokenRefreshSkew { get; init; } = TimeSpan.FromMinutes(1);
}
