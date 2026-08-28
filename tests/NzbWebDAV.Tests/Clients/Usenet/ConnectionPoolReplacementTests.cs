using System.Collections.Concurrent;
using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Tests.TestUtils;

namespace NzbWebDAV.Tests.Clients.Usenet;

public class ConnectionPoolReplacementTests
{
    [Fact]
    public async Task RepeatedReplacement_DisposesBeforeReconnectWithoutExceedingLimit()
    {
        var livePhysicalConnections = 0;
        var maxPhysicalConnections = 0;
        var disposed = 0;
        using var pool = new ConnectionPool<DisposableProbe>(
            maxConnections: 1,
            _ =>
            {
                var live = Interlocked.Increment(ref livePhysicalConnections);
                UpdateMaximum(ref maxPhysicalConnections, live);
                return ValueTask.FromResult(new DisposableProbe(() =>
                {
                    Interlocked.Decrement(ref livePhysicalConnections);
                    Interlocked.Increment(ref disposed);
                }));
            },
            replacementHandshakeSpacing: TimeSpan.FromMilliseconds(5));

        for (var i = 0; i < 12; i++)
        {
            using var connection = await pool.GetConnectionLockAsync(SemaphorePriority.High);
            connection.Replace("read-timeout-BODY");
        }

        Assert.Equal(12, disposed);
        Assert.Equal(0, livePhysicalConnections);
        Assert.Equal(0, pool.LiveConnections);
        Assert.Equal(1, maxPhysicalConnections);
        Assert.Equal(12, pool.GetChurn().ConnectionsDestroyed);
    }

    [Fact]
    public async Task ConcurrentReplacements_PaceNewHandshakes()
    {
        var connectionNumber = 0;
        var replacementCreatedAt = new ConcurrentQueue<long>();
        using var pool = new ConnectionPool<DisposableProbe>(
            maxConnections: 2,
            _ =>
            {
                var number = Interlocked.Increment(ref connectionNumber);
                if (number > 2)
                    replacementCreatedAt.Enqueue(Environment.TickCount64);
                return ValueTask.FromResult(new DisposableProbe(() => { }));
            },
            replacementHandshakeSpacing: TimeSpan.FromMilliseconds(100));

        var first = await pool.GetConnectionLockAsync(SemaphorePriority.High);
        var second = await pool.GetConnectionLockAsync(SemaphorePriority.High);
        first.Replace("read-timeout-BODY");
        second.Replace("read-timeout-ARTICLE");
        first.Dispose();
        second.Dispose();

        var replacements = await Task.WhenAll(
            pool.GetConnectionLockAsync(SemaphorePriority.High),
            pool.GetConnectionLockAsync(SemaphorePriority.High));
        foreach (var replacement in replacements) replacement.Dispose();

        var timestamps = replacementCreatedAt.ToArray();
        Assert.Equal(2, timestamps.Length);
        Assert.True(timestamps[1] - timestamps[0] >= 70,
            $"Replacement handshakes were only {timestamps[1] - timestamps[0]}ms apart.");
        Assert.Equal(2, pool.LiveConnections);
        Assert.Equal(2, pool.IdleConnections);
    }

    [Fact]
    public async Task ReplacementReservations_ExtendPacingWindowForLargeQueue()
    {
        const int poolWidth = 15;
        var clock = new SignalingTimeProvider();
        using var pool = new ConnectionPool<DisposableProbe>(
            maxConnections: poolWidth,
            _ => ValueTask.FromResult(new DisposableProbe(() => { })),
            replacementHandshakeSpacing: TimeSpan.FromSeconds(1),
            timeProvider: clock);

        var originals = await Task.WhenAll(Enumerable.Range(0, poolWidth)
            .Select(_ => pool.GetConnectionLockAsync(SemaphorePriority.High)));
        foreach (var original in originals)
        {
            original.Replace("read-timeout-BODY");
            original.Dispose();
        }

        var initialTimers = Enumerable.Range(0, 3)
            .Select(_ => clock.WaitForNextTimerAsync())
            .ToArray();
        var replacements = Enumerable.Range(0, poolWidth)
            .Select(_ => pool.GetConnectionLockAsync(SemaphorePriority.High))
            .ToArray();
        await Task.WhenAll(initialTimers).WaitAsync(TimeSpan.FromSeconds(1));

        // Each completed delay admits one queued borrower through the three-slot
        // handshake gate. The tenth admission crosses the original fixed window.
        for (var i = 0; i < poolWidth - 3; i++)
        {
            var nextTimer = clock.WaitForNextTimerAsync();
            clock.Advance(TimeSpan.FromSeconds(1));
            await nextTimer.WaitAsync(TimeSpan.FromSeconds(1));
        }

        var allReplacements = Task.WhenAll(replacements);
        for (var i = 0; i < 4 && !allReplacements.IsCompleted; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }
        var acquired = await allReplacements.WaitAsync(TimeSpan.FromSeconds(1));
        foreach (var replacement in acquired) replacement.Dispose();

        Assert.Equal(poolWidth, pool.LiveConnections);
        Assert.Equal(poolWidth, pool.IdleConnections);
    }

    [Fact]
    public async Task RepeatedHandshakeFailures_BackOffAndReleasePoolPermit()
    {
        var attempts = new ConcurrentQueue<long>();
        var attempt = 0;
        using var pool = new ConnectionPool<DisposableProbe>(
            maxConnections: 1,
            _ =>
            {
                attempts.Enqueue(Environment.TickCount64);
                if (Interlocked.Increment(ref attempt) <= 3)
                    throw new IOException("AUTHINFO failed");
                return ValueTask.FromResult(new DisposableProbe(() => { }));
            },
            replacementHandshakeSpacing: TimeSpan.FromMilliseconds(40));

        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<IOException>(async () =>
                await pool.GetConnectionLockAsync(SemaphorePriority.High));
            Assert.Equal(1, pool.AvailableConnections);
            Assert.Equal(0, pool.LiveConnections);
        }

        using (await pool.GetConnectionLockAsync(SemaphorePriority.High))
        {
            Assert.Equal(1, pool.LiveConnections);
        }

        var timestamps = attempts.ToArray();
        Assert.Equal(4, timestamps.Length);
        Assert.True(timestamps[1] - timestamps[0] >= 25,
            $"First handshake retry waited only {timestamps[1] - timestamps[0]}ms.");
        Assert.True(timestamps[2] - timestamps[1] >= 60,
            $"Second handshake retry waited only {timestamps[2] - timestamps[1]}ms.");
        Assert.True(timestamps[3] - timestamps[2] >= 120,
            $"Third handshake retry waited only {timestamps[3] - timestamps[2]}ms.");
        Assert.Equal(3, pool.GetChurn().HandshakeFailures);
    }

    [Fact]
    public async Task ConcurrentHandshakeFailures_PreserveLongestBackoffDeadline()
    {
        var attempts = 0;
        var releaseFailures = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var pool = new ConnectionPool<DisposableProbe>(
            maxConnections: 3,
            async _ =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                if (attempt <= 3)
                {
                    if (attempt == 3) releaseFailures.TrySetResult();
                    await releaseFailures.Task;
                    throw new IOException("AUTHINFO failed");
                }

                return new DisposableProbe(() => { });
            },
            replacementHandshakeSpacing: TimeSpan.FromMilliseconds(40));

        var failures = Enumerable.Range(0, 3)
            .Select(_ => Assert.ThrowsAsync<IOException>(async () =>
                await pool.GetConnectionLockAsync(SemaphorePriority.High)))
            .ToArray();
        await Task.WhenAll(failures);

        var retryStarted = Environment.TickCount64;
        using (await pool.GetConnectionLockAsync(SemaphorePriority.High))
        {
            Assert.Equal(1, pool.LiveConnections);
        }

        var retryDelay = Environment.TickCount64 - retryStarted;
        Assert.True(retryDelay >= 120,
            $"Concurrent handshake failures preserved only {retryDelay}ms of the longest backoff.");
        Assert.Equal(3, pool.GetChurn().HandshakeFailures);
    }

    [Fact]
    public async Task CancelledReplacementFactory_DoesNotCountFailureOrDelayNextBorrower()
    {
        var clock = new ControllableTimeProvider();
        var factoryCalls = 0;
        var cancelledFactoryStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var pool = new ConnectionPool<DisposableProbe>(
            maxConnections: 1,
            async cancellationToken =>
            {
                var call = Interlocked.Increment(ref factoryCalls);
                if (call == 2)
                {
                    cancelledFactoryStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                return new DisposableProbe(() => { });
            },
            replacementHandshakeSpacing: TimeSpan.FromSeconds(1),
            timeProvider: clock);

        var first = await pool.GetConnectionLockAsync(SemaphorePriority.High);
        first.Replace("read-timeout-BODY");
        first.Dispose();

        clock.Advance(TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource();
        var cancelledBorrow = pool.GetConnectionLockAsync(
            SemaphorePriority.High, cancellation.Token);
        await cancelledFactoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelledBorrow);

        Assert.Equal(0, pool.GetChurn().HandshakeFailures);
        Assert.Equal(1, pool.AvailableConnections);

        var laterBorrow = pool.GetConnectionLockAsync(SemaphorePriority.High);
        using (await laterBorrow.WaitAsync(TimeSpan.FromSeconds(1)))
        {
            Assert.Equal(1, pool.LiveConnections);
        }
    }

    [Fact]
    public async Task CancelledReplacementPacingWait_DoesNotDelayNextBorrower()
    {
        var clock = new SignalingTimeProvider();
        using var pool = new ConnectionPool<DisposableProbe>(
            maxConnections: 1,
            _ => ValueTask.FromResult(new DisposableProbe(() => { })),
            replacementHandshakeSpacing: TimeSpan.FromSeconds(1),
            timeProvider: clock);

        var first = await pool.GetConnectionLockAsync(SemaphorePriority.High);
        first.Replace("read-timeout-ARTICLE");
        first.Dispose();

        using var cancellation = new CancellationTokenSource();
        var cancelledDelayStarted = clock.WaitForNextTimerAsync();
        var cancelledBorrow = pool.GetConnectionLockAsync(
            SemaphorePriority.High, cancellation.Token);
        await cancelledDelayStarted.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelledBorrow);

        var laterDelayStarted = clock.WaitForNextTimerAsync();
        var laterBorrow = pool.GetConnectionLockAsync(SemaphorePriority.High);
        await laterDelayStarted.WaitAsync(TimeSpan.FromSeconds(1));
        clock.Advance(TimeSpan.FromSeconds(1));
        using (await laterBorrow.WaitAsync(TimeSpan.FromSeconds(1)))
        {
            Assert.Equal(1, pool.LiveConnections);
        }

        Assert.Equal(0, pool.GetChurn().HandshakeFailures);
    }

    private sealed class SignalingTimeProvider : TimeProvider
    {
        private readonly ControllableTimeProvider _inner = new();
        private readonly ConcurrentQueue<TaskCompletionSource> _timerWaiters = new();

        public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();
        public override long GetTimestamp() => _inner.GetTimestamp();
        public override long TimestampFrequency => _inner.TimestampFrequency;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = _inner.CreateTimer(callback, state, dueTime, period);
            if (_timerWaiters.TryDequeue(out var waiter))
                waiter.TrySetResult();
            return timer;
        }

        public Task WaitForNextTimerAsync()
        {
            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _timerWaiters.Enqueue(waiter);
            return waiter.Task;
        }

        public void Advance(TimeSpan delta) => _inner.Advance(delta);
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        var current = Volatile.Read(ref maximum);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref maximum, candidate, current);
            if (observed == current) return;
            current = observed;
        }
    }

    private sealed class DisposableProbe(Action onDispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                onDispose();
        }
    }
}
