using System.Globalization;
using Syscom.Load;

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
    long CompanyId,
    int PageSize = 500,
    int BufferedPages = 2)
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
        "SYSCOM_COMPANY_ID",
        "SYSCOM_PAGE_SIZE",
        "SYSCOM_BUFFERED_PAGES"
    ];

    public SyscomSyncOptions ToSyncOptions() => new()
    {
        MaximumDuration = MaximumDuration,
        RetentionDays = RetentionDays,
        CompanyId = CompanyId,
        PageSize = PageSize,
        BufferedPages = BufferedPages
    };

    public static SyscomAppConfiguration FromEnvironment(string connectionStringVariable = "CONNECTION_STRING")
    {
        var values = EnvironmentNames.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        values["CONNECTION_STRING"] = Environment.GetEnvironmentVariable(connectionStringVariable);
        try { return Parse(values); }
        catch (SyscomAppConfigurationException ex)
        {
            throw new SyscomAppConfigurationException(
                ex.Message.Replace("CONNECTION_STRING", connectionStringVariable, StringComparison.Ordinal));
        }
    }

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
            ReadPositiveInt("SYSCOM_COMPANY_ID", 1, 1, int.MaxValue),
            ReadPositiveInt("SYSCOM_PAGE_SIZE", 500, 10, 1000),
            ReadPositiveInt("SYSCOM_BUFFERED_PAGES", 2, 1, 16));

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
