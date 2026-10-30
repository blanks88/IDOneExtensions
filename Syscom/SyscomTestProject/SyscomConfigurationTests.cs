using Syscom;

namespace SyscomTestProject;

public class SyscomConfigurationTests
{
    private static Dictionary<string, string?> ValidValues() => new()
    {
        ["CONNECTION_STRING"] = "Host=localhost;Database=fixture",
        ["SYSCOM_CLIENT_ID"] = "fixture-id",
        ["SYSCOM_CLIENT_SECRET"] = "fixture-secret"
    };

    [Fact]
    public void Parse_ReportsOnlyMissingVariableNames()
    {
        var values = ValidValues();
        values.Remove("CONNECTION_STRING");
        values.Remove("SYSCOM_CLIENT_ID");

        var exception = Assert.Throws<SyscomAppConfigurationException>(
            () => SyscomAppConfiguration.Parse(values));

        Assert.Contains("CONNECTION_STRING", exception.Message);
        Assert.Contains("SYSCOM_CLIENT_ID", exception.Message);
        Assert.DoesNotContain("fixture-secret", exception.Message);
    }

    [Theory]
    [InlineData("http://developers.syscom.mx")]
    [InlineData("http://example.com")]
    [InlineData("https://example.com")]
    [InlineData("https://user:password@developers.syscom.mx")]
    public void Parse_RejectsUnsafeBaseUrl(string url)
    {
        var values = ValidValues();
        values["SYSCOM_BASE_URL"] = url;

        var exception = Assert.Throws<SyscomAppConfigurationException>(
            () => SyscomAppConfiguration.Parse(values));

        Assert.Contains("SYSCOM_BASE_URL", exception.Message);
        Assert.DoesNotContain(url, exception.Message);
    }

    [Fact]
    public void Parse_AcceptsLoopbackSimulatorAndOperationalSettings()
    {
        var values = ValidValues();
        values["SYSCOM_BASE_URL"] = "http://127.0.0.1:5100";
        values["SYSCOM_OAUTH_TOKEN_PATH"] = "/api/v1/oauth/token";
        values["SYSCOM_OAUTH_FORM_ENCODED"] = "1";
        values["SYSCOM_REQUEST_TIMEOUT_SECONDS"] = "15";
        values["SYSCOM_MAXIMUM_DURATION_SECONDS"] = "180";
        values["SYSCOM_RETENTION_DAYS"] = "14";
        values["SYSCOM_COMPANY_ID"] = "42";

        var config = SyscomAppConfiguration.Parse(values);

        Assert.Equal("http://127.0.0.1:5100", config.BaseUrl);
        Assert.Equal("/api/v1/oauth/token", config.OAuthTokenPath);
        Assert.True(config.UseFormEncodedTokenRequest);
        Assert.Equal(TimeSpan.FromSeconds(15), config.RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(180), config.MaximumDuration);
        Assert.Equal(14, config.RetentionDays);
        Assert.Equal(42, config.CompanyId);
    }

    [Theory]
    [InlineData("SYSCOM_REQUEST_TIMEOUT_SECONDS", "0")]
    [InlineData("SYSCOM_MAXIMUM_DURATION_SECONDS", "86401")]
    [InlineData("SYSCOM_RETENTION_DAYS", "-1")]
    [InlineData("SYSCOM_COMPANY_ID", "abc")]
    [InlineData("SYSCOM_OAUTH_TOKEN_PATH", "//example.com/token")]
    [InlineData("SYSCOM_OAUTH_FORM_ENCODED", "yes")]
    public void Parse_RejectsInvalidSettingsByName(string name, string value)
    {
        var values = ValidValues();
        values[name] = value;

        var exception = Assert.Throws<SyscomAppConfigurationException>(
            () => SyscomAppConfiguration.Parse(values));

        Assert.Contains(name, exception.Message);
        Assert.DoesNotContain(value, exception.Message);
    }
}
