using Microsoft.Extensions.Logging.Abstractions;
using Syscom.Load;
using Syscom.Models;

namespace SyscomTestProject;

public class SyscomPipelineTests
{
    private static readonly SyscomCategory[] OneCategory = [new() { Id = 7 }];

    [Theory]
    [InlineData(9, 2)]
    [InlineData(1001, 2)]
    [InlineData(500, 0)]
    [InlineData(500, 17)]
    public async Task InvalidPagingOptions_AreRejectedBeforeSyncSideEffects(int pageSize, int bufferedPages)
    {
        var worker = new SyscomWorker(NullLoggerFactory.Instance, null!, null!, new SyscomSyncOptions
        {
            PageSize = pageSize, BufferedPages = bufferedPages
        });
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => worker.RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DownloadOverlapsStaging_AndStagesPagesInOrder()
    {
        var stagingFirst = Gate();
        var allowFirstStage = Gate();
        var fetchedSecond = Gate();
        var staged = new List<int>();
        var activeWriters = 0;
        var maxWriters = 0;

        var run = SyscomPagePipeline.RunAsync(OneCategory, 2,
            async (category, page, ct) =>
            {
                Assert.Equal(7, category);
                if (page == 2)
                {
                    await stagingFirst.Task.WaitAsync(ct);
                    fetchedSecond.TrySetResult();
                }
                return Response(page, 3);
            },
            async (_, page, _, _, ct) =>
            {
                maxWriters = Math.Max(maxWriters, Interlocked.Increment(ref activeWriters));
                if (page == 1)
                {
                    stagingFirst.TrySetResult();
                    await allowFirstStage.Task.WaitAsync(ct);
                }
                staged.Add(page);
                Interlocked.Decrement(ref activeWriters);
            }, CancellationToken.None);

        await stagingFirst.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fetchedSecond.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(staged);
        allowFirstStage.TrySetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { 1, 2, 3 }, staged);
        Assert.Equal(1, maxWriters);
    }

    [Fact]
    public async Task BoundedChannel_BlocksProducerWhileConsumerAndBufferAreOccupied()
    {
        var stagingFirst = Gate();
        var allowFirstStage = Gate();
        var fetchedThird = Gate();
        var fetchedFourth = Gate();
        var fetchCount = 0;

        var run = SyscomPagePipeline.RunAsync(OneCategory, 1,
            (_, page, _) =>
            {
                Interlocked.Increment(ref fetchCount);
                if (page == 3) fetchedThird.TrySetResult();
                if (page == 4) fetchedFourth.TrySetResult();
                return Task.FromResult(Response(page, 4));
            },
            async (_, page, _, _, ct) =>
            {
                if (page == 1)
                {
                    stagingFirst.TrySetResult();
                    await allowFirstStage.Task.WaitAsync(ct);
                }
            }, CancellationToken.None);

        await stagingFirst.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fetchedThird.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(fetchedFourth.Task.IsCompleted);
        Assert.Equal(3, Volatile.Read(ref fetchCount));
        allowFirstStage.TrySetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(fetchedFourth.Task.IsCompleted);
    }

    [Fact]
    public async Task CancellationWhileProducerBlocked_StopsBothTasks()
    {
        using var cancel = new CancellationTokenSource();
        var stagingFirst = Gate();
        var fetchedThird = Gate();
        var active = 0;
        var fetched = 0;

        var run = SyscomPagePipeline.RunAsync(OneCategory, 1,
            (_, page, _) =>
            {
                Interlocked.Increment(ref fetched);
                if (page == 3) fetchedThird.TrySetResult();
                return Task.FromResult(Response(page, 4));
            },
            async (_, page, _, _, ct) =>
            {
                Interlocked.Increment(ref active);
                try
                {
                    if (page == 1)
                    {
                        stagingFirst.TrySetResult();
                        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    }
                }
                finally { Interlocked.Decrement(ref active); }
            }, cancel.Token);

        await stagingFirst.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fetchedThird.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, Volatile.Read(ref active));
        Assert.Equal(3, Volatile.Read(ref fetched));
    }

    [Fact]
    public async Task ConsumerFailure_CancelsBlockedProducer_AndDoesNotPromote()
    {
        var stageStarted = Gate();
        var failStage = Gate();
        var fetchSecondStarted = Gate();
        var fetchStopped = Gate();
        var promoted = false;
        var stagingError = new InvalidOperationException("staging failed");

        async Task RunAndPromoteAsync()
        {
            await SyscomPagePipeline.RunAsync(OneCategory, 1,
                async (_, page, ct) =>
                {
                    if (page == 2)
                    {
                        fetchSecondStarted.TrySetResult();
                        try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                        finally { fetchStopped.TrySetResult(); }
                    }
                    return Response(page, 2);
                },
                async (_, _, _, _, ct) =>
                {
                    stageStarted.TrySetResult();
                    await failStage.Task.WaitAsync(ct);
                    throw stagingError;
                }, CancellationToken.None);
            promoted = true;
        }

        var run = RunAndPromoteAsync();
        await stageStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fetchSecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        failStage.TrySetResult();
        Assert.Same(stagingError, await Assert.ThrowsAsync<InvalidOperationException>(
            () => run.WaitAsync(TimeSpan.FromSeconds(5))));
        Assert.True(fetchStopped.Task.IsCompleted);
        Assert.False(promoted);
    }

    [Fact]
    public async Task ProducerFailure_CancelsConsumer_AndPreservesOriginalError()
    {
        var stageStarted = Gate();
        var stageStopped = Gate();
        var failFetch = Gate();
        var fetchSecondStarted = Gate();
        var fetchError = new InvalidOperationException("download failed");
        var promoted = false;

        async Task RunAndPromoteAsync()
        {
            await SyscomPagePipeline.RunAsync(OneCategory, 1,
                async (_, page, ct) =>
                {
                    if (page == 2)
                    {
                        fetchSecondStarted.TrySetResult();
                        await failFetch.Task.WaitAsync(ct);
                        throw fetchError;
                    }
                    return Response(page, 2);
                },
                async (_, _, _, _, ct) =>
                {
                    stageStarted.TrySetResult();
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                    finally { stageStopped.TrySetResult(); }
                }, CancellationToken.None);
            promoted = true;
        }

        var run = RunAndPromoteAsync();
        await stageStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fetchSecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        failFetch.TrySetResult();
        Assert.Same(fetchError, await Assert.ThrowsAsync<InvalidOperationException>(
            () => run.WaitAsync(TimeSpan.FromSeconds(5))));
        Assert.True(stageStopped.Task.IsCompleted);
        Assert.False(promoted);
    }

    [Fact]
    public async Task IncompleteCategory_DoesNotPromote()
    {
        var promoted = false;
        async Task RunAndPromoteAsync()
        {
            await SyscomPagePipeline.RunAsync(OneCategory, 2,
                (_, page, _) => Task.FromResult(Response(page, 2, total: 3)),
                (_, _, _, _, _) => Task.CompletedTask, CancellationToken.None);
            promoted = true;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(RunAndPromoteAsync);
        Assert.False(promoted);
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static SyscomProductsSearchResponse Response(int page, int pages, int? total = null) => new()
    {
        PaginaActual = page,
        Paginas = pages,
        Total = total ?? pages,
        Products = [new SyscomProduct { ProductoId = page, Modelo = $"P-{page}" }]
    };
}
