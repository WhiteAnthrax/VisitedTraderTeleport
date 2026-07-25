using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class TravelTransitionTimingTests
{
    [Fact]
    public void GetTeleportDelay_NullSettings_ReturnsZero()
    {
        float delay = TravelTransitionTiming.GetTeleportDelay(null);

        Assert.Equal(0f, delay);
    }

    [Fact]
    public void GetTeleportDelay_ZeroOrNegativeDuration_ReturnsZero()
    {
        var settings = new TravelTransitionSettings { DurationSeconds = 0f };

        Assert.Equal(0f, TravelTransitionTiming.GetTeleportDelay(settings));

        settings.DurationSeconds = -3f;
        Assert.Equal(0f, TravelTransitionTiming.GetTeleportDelay(settings));
    }

    [Fact]
    public void GetTeleportDelay_ShortDuration_ReturnsThirtyFivePercent()
    {
        var settings = new TravelTransitionSettings { DurationSeconds = 2f };

        float delay = TravelTransitionTiming.GetTeleportDelay(settings);

        Assert.Equal(0.7f, delay, 3);
    }

    [Fact]
    public void GetTeleportDelay_LongDuration_ClampsToMaxDelay()
    {
        var settings = new TravelTransitionSettings { DurationSeconds = 20f };

        float delay = TravelTransitionTiming.GetTeleportDelay(settings);

        Assert.Equal(1.5f, delay, 3);
    }

    [Fact]
    public void GetTransitionHoldAfterTeleport_ShortDuration_ClampsToArrivalLead()
    {
        var settings = new TravelTransitionSettings { DurationSeconds = 0.5f };

        float hold = TravelTransitionTiming.GetTransitionHoldAfterTeleport(settings);

        Assert.Equal(0.35f, hold, 3);
    }

    [Fact]
    public void GetTransitionHoldAfterTeleport_LongDuration_ReturnsRemainderAfterDelay()
    {
        var settings = new TravelTransitionSettings { DurationSeconds = 20f };

        float hold = TravelTransitionTiming.GetTransitionHoldAfterTeleport(settings);

        // delay clamps to 1.5s, so hold = 20 - 1.5 = 18.5
        Assert.Equal(18.5f, hold, 3);
    }

    [Fact]
    public void GetTransitionHoldAfterTeleport_NullSettings_ReturnsArrivalLead()
    {
        float hold = TravelTransitionTiming.GetTransitionHoldAfterTeleport(null);

        Assert.Equal(0.35f, hold, 3);
    }
}
