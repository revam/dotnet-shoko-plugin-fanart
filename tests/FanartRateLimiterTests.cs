using System;
using System.Threading.Tasks;
using Shoko.Plugin.Fanart.Api;
using Xunit;

namespace Shoko.Plugin.Fanart.Tests;

/// <summary>
/// The politeness budget in front of Fanart.tv.
/// </summary>
public class FanartRateLimiterTests
{
    [Fact]
    public void ANewLimiterStartsFull()
    {
        using var limiter = new FanartRateLimiter(maxTokens: 5, tokensPerSecond: 2, timeProvider: new ManualTimeProvider(DateTimeOffset.UnixEpoch));

        Assert.Equal(5, limiter.AvailableTokens);
    }

    [Fact]
    public async Task EachCallSpendsAToken()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        using var limiter = new FanartRateLimiter(maxTokens: 5, tokensPerSecond: 2, timeProvider: time);

        await limiter.WaitAsync(TestContext.Current.CancellationToken);
        limiter.Release();

        Assert.Equal(4, limiter.AvailableTokens);
    }

    [Fact]
    public async Task TokensComeBackOverTime()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        using var limiter = new FanartRateLimiter(maxTokens: 5, tokensPerSecond: 2, timeProvider: time);

        for (var i = 0; i < 5; i++)
        {
            await limiter.WaitAsync(TestContext.Current.CancellationToken);
            limiter.Release();
        }

        Assert.Equal(0, limiter.AvailableTokens);

        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(2, limiter.AvailableTokens);
    }

    [Fact]
    public void RefillsStopAtTheBurstCapacity()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        using var limiter = new FanartRateLimiter(maxTokens: 5, tokensPerSecond: 2, timeProvider: time);

        time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(5, limiter.AvailableTokens);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ALimiterThatWouldNeverLetAnythingThroughIsRefused(int maxTokens)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FanartRateLimiter(maxTokens: maxTokens));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FanartRateLimiter(tokensPerSecond: maxTokens));
    }
}
