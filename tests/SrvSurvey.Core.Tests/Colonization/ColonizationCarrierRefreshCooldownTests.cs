using System.Text.Json;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationCarrierRefreshCooldownTests
{
    [Fact]
    public void AttemptsAreIndependentPerCarrierAndBecomeAvailableAtExactlyFiveMinutes()
    {
        var clock = new Clock();
        var cooldown = new ColonizationCarrierRefreshCooldown(timeProvider: clock);
        var first = new ColonizationFleetCarrier { MarketId = 42 };
        ColonizationFleetCarrier second = first with { MarketId = 43 };
        Assert.True(cooldown.TryBegin(first));
        Assert.False(cooldown.TryBegin(first));
        Assert.Equal(TimeSpan.FromMinutes(5), cooldown.Remaining(first));
        Assert.True(cooldown.TryBegin(second));
        clock.Now += TimeSpan.FromMinutes(5) - TimeSpan.FromMilliseconds(1);
        Assert.False(cooldown.TryBegin(first));
        clock.Now += TimeSpan.FromMilliseconds(1);
        Assert.Equal(TimeSpan.Zero, cooldown.Remaining(first));
        Assert.True(cooldown.TryBegin(first));
        Assert.True(cooldown.TryBegin(second));
        Assert.Throws<ArgumentNullException>(() => cooldown.Remaining(null!));
        Assert.Throws<ArgumentNullException>(() => cooldown.TryBegin(null!));
    }

    [Fact]
    public void PersistedAttemptsAndServerRefreshTimesBothSurviveANewEditorSession()
    {
        var clock = new Clock();
        var attempts = new Dictionary<long, DateTimeOffset>();
        var first = new ColonizationCarrierRefreshCooldown(
            id => attempts.GetValueOrDefault(id),
            (id, time) => attempts[id] = time,
            clock
        );
        var carrier = new ColonizationFleetCarrier { MarketId = 42 };
        Assert.True(first.TryBegin(carrier));
        var reopened = new ColonizationCarrierRefreshCooldown(
            id => attempts.GetValueOrDefault(id),
            timeProvider: clock
        );
        Assert.False(reopened.TryBegin(carrier));
        clock.Now += TimeSpan.FromMinutes(5);
        ColonizationFleetCarrier serverUpdated = carrier with { LastRefresh = clock.Now - TimeSpan.FromMinutes(2) };
        Assert.Equal(TimeSpan.FromMinutes(3), reopened.Remaining(serverUpdated));
        clock.Now += TimeSpan.FromMinutes(3);
        Assert.True(reopened.TryBegin(serverUpdated));
        ColonizationFleetCarrier parsed = JsonSerializer.Deserialize<ColonizationFleetCarrier>(
            """{"marketId":42,"lastRefresh":"2026-10-17T03:40:46.1588546+00:00"}"""
        )!;
        Assert.Equal(2026, parsed.LastRefresh!.Value.Year);
    }

    [Fact]
    public void SharedEditorsCannotBeginTwoRefreshesAtOnce()
    {
        var cooldown = new ColonizationCarrierRefreshCooldown();
        var carrier = new ColonizationFleetCarrier { MarketId = 42 };
        int allowed = 0;
        Parallel.For(
            0,
            20,
            _ =>
            {
                if (cooldown.TryBegin(carrier))
                {
                    Interlocked.Increment(ref allowed);
                }
            }
        );
        Assert.Equal(1, allowed);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
