using System.Text.Json;
using Syscom;
using Syscom.Models;
using Xunit.Abstractions;

namespace SyscomTestProject;

public class SyscomClientTests(ITestOutputHelper testOutputHelper)
{
    private readonly SyscomClient _client = new(new HttpClient(),
        new SyscomClientOptions("s478EpzmgpnoaIw7Q3YVAdLpaEAF3h8L",
            "RPFBY1PHDosiHRpy5K5CGQTxmwufB1ZrJMKx5JRn")
    );

    [Fact(Skip = "This test requires a valid Syscom API key")]
    public async Task GetAccessTokenAsync_ShouldReturnAccessToken()
    {
        // arrange
        // act
        var result = await _client.GetAccessTokenAsync();
        
        // assert
        Assert.NotNull(result);
        Assert.NotNull(result.AccessToken);
        
        testOutputHelper.WriteLine($"Access token was received: {result.AccessToken}");
    }

    [Fact(Skip = "This test requires a valid Syscom API key")]
    public async Task GetCategoriesAsync_ShouldReturnCategories()
    {
        // arrange
        // act
        var result = await _client.GetCategoriesAsync();
        
        // assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        
        testOutputHelper.WriteLine("Categories where received:");
        testOutputHelper.WriteLine(JsonSerializer.Serialize(result));
    }

    [Fact(Skip = "This test requires a valid Syscom API key")]
    public async Task GetProductsAsync_ShouldReturnProducts()
    {
        // arrange
        var @params = new SyscomProductsRequest(22);
        
        // act
        var result = await _client.GetProductsAsync(@params);
        
        // assert
        Assert.NotNull(result);
        Assert.NotNull(result.Products);
        Assert.NotEmpty(result.Products);
        
        testOutputHelper.WriteLine("Products where received:");
        testOutputHelper.WriteLine(JsonSerializer.Serialize(result));
    }
}