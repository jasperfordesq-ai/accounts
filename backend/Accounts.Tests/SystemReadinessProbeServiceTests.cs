using Accounts.Api.Services;
using Xunit;

namespace Accounts.Tests;

public sealed class SystemReadinessProbeServiceTests
{
    [Fact]
    public async Task ConcurrentCallersShareOneProbeAndSuccessExpiresAfterFiveSeconds()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));
        var calls = 0;
        var service = new SystemReadinessProbeService(async cancellationToken =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(50, cancellationToken);
            return new SystemReadinessProbe(true, "reachable", "current", "configured");
        }, clock);

        var probes = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => service.GetAsync()));

        Assert.Equal(1, calls);
        Assert.All(probes, probe => Assert.True(probe.Ready));
        Assert.True((await service.GetAsync()).Ready);
        Assert.Equal(1, calls);

        clock.UtcNow = clock.UtcNow.AddSeconds(6);
        Assert.True((await service.GetAsync()).Ready);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ProbeFailureIsSafeAndRetriedAfterShortFailureCache()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));
        var calls = 0;
        var fail = true;
        var service = new SystemReadinessProbeService(_ =>
        {
            calls++;
            return fail
                ? throw new InvalidOperationException("sensitive database failure")
                : Task.FromResult(new SystemReadinessProbe(true, "reachable", "current", "configured"));
        }, clock);

        var unavailable = await service.GetAsync();
        Assert.False(unavailable.Ready);
        Assert.Equal("schema_unavailable", unavailable.Database);
        Assert.Equal(1, calls);

        fail = false;
        Assert.False((await service.GetAsync()).Ready);
        Assert.Equal(1, calls);
        clock.UtcNow = clock.UtcNow.AddSeconds(2);
        Assert.True((await service.GetAsync()).Ready);
        Assert.Equal(2, calls);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
