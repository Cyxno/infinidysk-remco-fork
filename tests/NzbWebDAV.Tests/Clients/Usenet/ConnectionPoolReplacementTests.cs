using System.Collections.Concurrent;
using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Clients.Usenet.Connections;

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
