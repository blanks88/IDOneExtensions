using System.Globalization;

namespace Syscom;

public sealed class SyscomAppConfigurationException(string message) : Exception(message);

public sealed record SyscomAppConfiguration(
    string ConnectionString,
    string ClientId,
    string ClientSecret,
    string BaseUrl,
    string OAuthTokenPath,
    bool UseFormEncodedTokenRequest,
    TimeSpan RequestTimeout,
    TimeSpan MaximumDuration,
    int RetentionDays,
    long CompanyId)
{
    private static readonly string[] EnvironmentNames =
    [
        "CONNECTION_STRING",
        "SYSCOM_CLIENT_ID",
        "SYSCOM_CLIENT_SECRET",
        "SYSCOM_BASE_URL",
        "SYSCOM_OAUTH_TOKEN_PATH",
        "SYSCOM_OAUTH_FORM_ENCODED",
        "SYSCOM_REQUEST_TIMEOUT_SECONDS",
        "SYSCOM_MAXIMUM_DURATION_SECONDS",
        "SYSCOM_RETENTION_DAYS",
        "SYSCOM_COMPANY_ID"
    ];

    public static SyscomAppConfiguration FromEnvironment()
        => Parse(EnvironmentNames.ToDictionary(name => name, Environment.GetEnvironmentVariable));

    public static SyscomAppConfiguration Parse(IReadOnlyDictionary<string, string?> values)
    {
        string? Get(string name) => values.TryGetValue(name, out var value) ? value : null;
        var missing = new[] { "CONNECTION_STRING", "SYSCOM_CLIENT_ID", "SYSCOM_CLIENT_SECRET" }
            .Where(name => string.IsNullOrWhiteSpace(Get(name)))
            .ToArray();
        if (missing.Length > 0)
            throw new SyscomAppConfigurationException(
                $"Missing required environment variable(s): {string.Join(", ", missing)}.");

        var baseUrl = Get("SYSCOM_BASE_URL") ?? "https://developers.syscom.mx";
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
            !uri.IsLoopback && uri.Host != "developers.syscom.mx" ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            throw Invalid("SYSCOM_BASE_URL");

        var tokenPath = Get("SYSCOM_OAUTH_TOKEN_PATH") ?? "/oauth/token";
        if (!tokenPath.StartsWith('/') || tokenPath.StartsWith("//") ||
            tokenPath.Contains('?') || tokenPath.Contains('#') ||
            tokenPath.Contains('\\') || tokenPath.Any(char.IsControl))
            throw Invalid("SYSCOM_OAUTH_TOKEN_PATH");

        var formEncoded = Get("SYSCOM_OAUTH_FORM_ENCODED") switch
        {
            null or "" or "false" or "0" => false,
            "true" or "1" => true,
            _ => throw Invalid("SYSCOM_OAUTH_FORM_ENCODED")
        };

        var requestTimeout = ReadPositiveInt("SYSCOM_REQUEST_TIMEOUT_SECONDS", 30, 1, 300);
        var maximumDuration = ReadPositiveInt("SYSCOM_MAXIMUM_DURATION_SECONDS", 1800, 1, 86400);
        if (requestTimeout > maximumDuration)
            throw Invalid("SYSCOM_REQUEST_TIMEOUT_SECONDS");

        return new SyscomAppConfiguration(
            Get("CONNECTION_STRING")!,
            Get("SYSCOM_CLIENT_ID")!,
            Get("SYSCOM_CLIENT_SECRET")!,
            uri.GetLeftPart(UriPartial.Path).TrimEnd('/'),
            tokenPath,
            formEncoded,
            TimeSpan.FromSeconds(requestTimeout),
            TimeSpan.FromSeconds(maximumDuration),
            ReadPositiveInt("SYSCOM_RETENTION_DAYS", 30, 1, 3650),
            ReadPositiveInt("SYSCOM_COMPANY_ID", 1, 1, int.MaxValue));

        int ReadPositiveInt(string name, int defaultValue, int minimum, int maximum)
        {
            var raw = Get(name);
            if (raw is null or "") return defaultValue;
            if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var result) ||
                result < minimum || result > maximum)
                throw Invalid(name);
            return result;
        }
    }

    private static SyscomAppConfigurationException Invalid(string name)
        => new($"Invalid environment variable: {name}.");
}
