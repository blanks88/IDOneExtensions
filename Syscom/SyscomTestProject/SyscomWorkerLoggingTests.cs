using IDOneRepository;
using Microsoft.Extensions.Logging;
using Moq;
using Syscom;
using Syscom.Load;
using Syscom.Models;

namespace SyscomTestProject;

[Collection("Postgres sync")]
public class SyscomWorkerLoggingTests
{
    [PostgresTheory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancellation")]
    public async Task RunAsync_LogsActualOutcomeWithoutExceptionDetails(string outcome)
    {
        await using var fixture = await PostgresSyncFixture.CreateAsync();
        await using var uow = fixture.CreateUnitOfWork();
        var logger = new Mock<ILogger>();
        logger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        var messages = new List<(LogLevel Level, string Message)>();
        logger.Setup(x => x.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(call =>
                messages.Add(((LogLevel)call.Arguments[0], call.Arguments[2].ToString()!))));
        var factory = new Mock<ILoggerFactory>();
        factory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(logger.Object);
        var client = new Mock<ISyscomClient>();
        using var cts = new CancellationTokenSource();
        if (outcome == "success")
            client.Setup(x => x.GetCategoriesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SyscomCategory>());
        else if (outcome == "failure")
            client.Setup(x => x.GetCategoriesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("sensitive provider payload"));
        else
            client.Setup(x => x.GetCategoriesAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) =>
                {
                    cts.Cancel();
                    return Task.FromCanceled<List<SyscomCategory>>(token);
                });
        var worker = new SyscomWorker(factory.Object, client.Object, uow);
        if (outcome == "success")
            await worker.RunAsync(cts.Token);
        else if (outcome == "failure")
            await Assert.ThrowsAsync<InvalidOperationException>(() => worker.RunAsync(cts.Token));
        else
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.RunAsync(cts.Token));

        var finalMessages = messages.Where(x => x.Message.StartsWith("Syscom sync ") &&
            !x.Message.StartsWith("Syscom sync started")).ToList();
        var final = Assert.Single(finalMessages);
        Assert.Equal(outcome switch
        {
            "success" => LogLevel.Information,
            "failure" => LogLevel.Error,
            _ => LogLevel.Warning
        }, final.Level);
        Assert.Contains(outcome switch
        {
            "success" => "completed successfully",
            "failure" => "failed",
            _ => "canceled"
        }, final.Message);
        Assert.DoesNotContain("sensitive provider payload", final.Message);
    }
}
