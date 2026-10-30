using Syscom.Load;
using Syscom.Models;

namespace SyscomTestProject;

public class SyscomFailureDiagnosticsTests
{
    [Fact]
    public void PostgresFailure_ReportsSqlStateWithoutServerPayload()
    {
        const string secret = "Password=do-not-log";
        var error = new Npgsql.PostgresException(secret,"ERROR","ERROR","21000",detail:secret,hint:secret);
        var reason = SyscomFailureDiagnostics.Describe(error);
        Assert.Contains("sqlstate=21000",reason);
        Assert.Contains("CardinalityViolation",reason);
        Assert.DoesNotContain(secret,reason);
        var malformed = new Npgsql.PostgresException(secret,"ERROR","ERROR",secret);
        Assert.DoesNotContain(secret,SyscomFailureDiagnostics.Describe(malformed));
    }

    [Fact]
    public async Task StagingCollision_ReportsCurrentPageAndBothIds()
    {
        var collision = SyscomFailureDiagnostics.Validation(SyscomFailureReason.ModelCodeCollision,
            existingProductId: 101, incomingProductId: 202, existingCategory: 22);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SyscomPagePipeline.RunAsync([new SyscomCategory { Id = 26 }], 1,
                (_, page, _) => Task.FromResult(new SyscomProductsSearchResponse
                {
                    PaginaActual = page, Paginas = 2, Total = 2,
                    Products = [new SyscomProduct { ProductoId = page }]
                }), (_, page, _, _, _) => page == 2 ? Task.FromException(collision) : Task.CompletedTask,
                CancellationToken.None));
        Assert.Same(collision, error);
        var reason = SyscomFailureDiagnostics.Describe(error);
        Assert.Contains("category=26", reason);
        Assert.Contains("requested_page=2", reason);
        Assert.Contains("existing_product_id=101", reason);
        Assert.Contains("incoming_product_id=202", reason);
        Assert.Contains("existing_category=22", reason);
    }

    [Fact]
    public async Task ChangedTotal_ReportsSafeCategoryPageAndCounts()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SyscomPagePipeline.RunAsync([new SyscomCategory { Id = 26 }], 2,
                (_, page, _) => Task.FromResult(new SyscomProductsSearchResponse
                {
                    PaginaActual = page, Paginas = 15, Total = page == 1 ? 7500 : 7499,
                    Products = [new SyscomProduct { Modelo = "Password=secret-model" }]
                }), (_, _, _, _, _) => Task.CompletedTask, CancellationToken.None));
        var reason = SyscomFailureDiagnostics.Describe(error);
        Assert.Contains("ProductTotalChanged", reason);
        Assert.Contains("category=26", reason);
        Assert.Contains("requested_page=2", reason);
        Assert.Contains("expected_count=7500", reason);
        Assert.Contains("actual_count=7499", reason);
        Assert.DoesNotContain("secret-model", reason);
    }

    [Fact]
    public void UnexpectedErrors_DoNotExposeMessagesInnerExceptionsOrData()
    {
        const string secret = "Password=secret;Bearer token;https://user:pass@example.com";
        Exception[] errors =
        [
            new InvalidOperationException(secret, new Exception(secret)),
            new HttpRequestException(secret, new Exception(secret), System.Net.HttpStatusCode.Unauthorized),
            new System.Text.Json.JsonException(secret)
        ];
        foreach (var error in errors)
        {
            error.Data["Reason"] = secret;
            Assert.DoesNotContain(secret, SyscomFailureDiagnostics.Describe(error));
        }
        Assert.Contains("status=401", SyscomFailureDiagnostics.Describe(errors[1]));
    }

    [Fact]
    public void Collision_ReportsReasonWithoutProductContent()
    {
        var error = SyscomFailureDiagnostics.Validation(SyscomFailureReason.ModelCodeCollision, category: 26);
        var reason = SyscomFailureDiagnostics.Describe(error);
        Assert.Contains("ModelCodeCollision", reason);
        Assert.Contains("category=26", reason);
    }
}
