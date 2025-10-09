namespace Syscom;

public record SyscomClientOptions(string ClientId, string ClientSecret)
{
    // Base API URL, default to developers.syscom.mx
    public string BaseUrl { get; } = "https://developers.syscom.mx";

    // OAuth endpoint relative path
    public string OAuthTokenPath { get; } = "/oauth/token";

    // API base path
    public string ApiBasePath { get; } = "/api/v1";

    // OAuth access token
    public string? AccessToken { get; set; }
}