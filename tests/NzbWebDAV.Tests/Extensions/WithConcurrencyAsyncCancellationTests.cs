using NzbWebDAV.Extensions;

namespace NzbWebDAV.Tests.Extensions;

public class WithConcurrencyAsyncCancellationTests
{
    [Fact]
    public async Task WithConcurrencyAsync_ThrowsWhenTokenAlreadyCancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var tasks = Enumerable.Range(0, 5).Select(i => Task.FromResult(i));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in tasks.WithConcurrencyAsync(2, cts.Token).ConfigureAwait(false))
            {
                // Drain; the pre-cancelled token aborts enumeration before any item arrives.
            }
        });
    }

    [Fact]
    public async Task WithConcurrencyAsync_CompletesWithoutToken()
    {
        var tasks = Enumerable.Range(0, 5).Select(i => Task.FromResult(i));

        var results = new List<int>();
        await foreach (var value in tasks.WithConcurrencyAsync(2).ConfigureAwait(false))
            results.Add(value);

        Assert.Equal([0, 1, 2, 3, 4], results.OrderBy(x => x).ToList());
    }

    [Fact]
    public async Task WithConcurrencyAsync_DrainsInFlightTasksOnConsumerBreak()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = new[]
        {
            Task.FromResult(1),
            HoldUntilReleased(gate, drained),
        };

        var enumerator = tasks.WithConcurrencyAsync(2).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(1, enumerator.Current);

        // Break without consuming the in-flight task. DisposeAsync drains via finally,
        // so kick disposal off, then release the held task so drain can finish.
        var dispose = enumerator.DisposeAsync().AsTask();
        gate.TrySetResult();
        await dispose.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(drained.Task.IsCompleted);
    }

    [Fact]
    public async Task WithConcurrencyAsync_DrainsInFlightTasksOnFault()
    {
        var siblingCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSibling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = new[]
        {
            Task.FromException<int>(new InvalidOperationException("boom")),
            CompleteAfterRelease(releaseSibling, siblingCompleted),
            Task.FromResult(3),
        };

        // Kick off enumeration; WhenAny will surface the faulted task first once
        // concurrency is reached. Release the sibling so drain can finish.
        var enumerate = EnumerateAll(tasks.WithConcurrencyAsync(2));
        releaseSibling.TrySetResult();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => enumerate);
        Assert.Equal("boom", ex.Message);
        Assert.True(siblingCompleted.Task.IsCompleted);
    }

    [Fact]
    public async Task WithConcurrencyAsync_DrainsInFlightTasksOnCancellation()
    {
        using var cts = new CancellationTokenSource();
        var drainStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDrain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = new[]
        {
            Task.FromResult(1),
            HoldUntilReleased(releaseDrain, drained, drainStarted),
            Task.FromResult(3),
            Task.FromResult(4),
        };

        var enumerate = Task.Run(async () =>
        {
            await foreach (var value in tasks.WithConcurrencyAsync(2, cts.Token).ConfigureAwait(false))
            {
                if (value == 1)
                    await cts.CancelAsync().ConfigureAwait(false);
            }
        });

        // Wait until the in-flight hold task is actually running, then release it
        // so the finally drain can complete and surface the cancellation.
        await drainStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseDrain.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => enumerate);
        Assert.True(drained.Task.IsCompleted);
    }

    private static async Task<List<int>> EnumerateAll(IAsyncEnumerable<int> source)
    {
        var results = new List<int>();
        await foreach (var value in source.ConfigureAwait(false))
            results.Add(value);
        return results;
    }

    private static async Task<int> HoldUntilReleased(
        TaskCompletionSource release,
        TaskCompletionSource drained,
        TaskCompletionSource? started = null)
    {
        started?.TrySetResult();
        try
        {
            await release.Task.ConfigureAwait(false);
            return 2;
        }
        finally
        {
            drained.TrySetResult();
        }
    }

    private static async Task<int> CompleteAfterRelease(
        TaskCompletionSource release,
        TaskCompletionSource completed)
    {
        try
        {
            await release.Task.ConfigureAwait(false);
            return 2;
        }
        finally
        {
            completed.TrySetResult();
        }
    }
}
