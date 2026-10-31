using System.Net;
using System.Text;
using Syscom;
using Syscom.Models;

namespace SyscomTestProject;

public class SyscomPagingOptionsTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(500)]
    [InlineData(1000)]
    public async Task ProductsRequest_EmitsExplicitLimit(int limit)
    {
        var requests = new List<Uri>();
        var client = CreateClient(requests);

        await client.GetProductsAsync(new SyscomProductsRequest(Categoria: 22, Pagina: 3, Limit: limit));

        var uri = Assert.Single(requests);
        Assert.Equal("/api/v1/productos", uri.AbsolutePath);
        Assert.Contains("categoria=22", uri.Query);
        Assert.Contains("pagina=3", uri.Query);
        Assert.Contains($"limit={limit}", uri.Query);
    }

    [Fact]
    public async Task ProductsRequest_OmitsLimitForExistingCallers()
    {
        var requests = new List<Uri>();
        var client = CreateClient(requests);

        await client.GetProductsAsync(new SyscomProductsRequest(22));

        Assert.DoesNotContain("limit=", Assert.Single(requests).Query);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(1001)]
    public async Task ProductsRequest_RejectsInvalidLimitBeforeHttp(int limit)
    {
        var requests = new List<Uri>();
        var client = CreateClient(requests);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.GetProductsAsync(new SyscomProductsRequest(Limit: limit)));

        Assert.Empty(requests);
    }

    private static SyscomClient CreateClient(List<Uri> requests)
    {
        var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });
        return new SyscomClient(new HttpClient(handler), new SyscomClientOptions("fixture-id", "fixture-secret")
        {
            BaseUrl = "https://developers.syscom.mx",
            AccessToken = "fixture-token"
        });
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(send(request));
    }
}
