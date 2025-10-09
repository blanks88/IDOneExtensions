using Microsoft.Extensions.Logging;
using Moq;
using RichardSzalay.MockHttp;

namespace SyscomTestProject;

internal static class HttpTestHelpers
{
    public static (HttpClient client, MockHttpMessageHandler handler) CreateMockHttp(string baseUrl)
    {
        var handler = new MockHttpMessageHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
        return (client, handler);
    }

    public static ILogger<T> CreateLogger<T>() => Mock.Of<ILogger<T>>();
}