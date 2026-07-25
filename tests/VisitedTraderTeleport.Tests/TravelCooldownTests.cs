using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class TravelCooldownTests
{
    [Fact]
    public void GetRemainingSeconds_JustTraveled_ReturnsFullCooldown()
    {
        float remaining = TravelCooldown.GetRemainingSeconds(currentTime: 100f, lastTravelTime: 100f);

        Assert.Equal(10f, remaining);
    }

    [Fact]
    public void GetRemainingSeconds_HalfwayThroughCooldown_ReturnsRemainder()
    {
        float remaining = TravelCooldown.GetRemainingSeconds(currentTime: 104f, lastTravelTime: 100f);

        Assert.Equal(6f, remaining, 3);
    }

    [Fact]
    public void GetRemainingSeconds_ExactlyElapsed_ReturnsZero()
    {
        float remaining = TravelCooldown.GetRemainingSeconds(currentTime: 110f, lastTravelTime: 100f);

        Assert.Equal(0f, remaining, 3);
    }

    [Fact]
    public void GetRemainingSeconds_PastCooldown_ReturnsNegative()
    {
        float remaining = TravelCooldown.GetRemainingSeconds(currentTime: 130f, lastTravelTime: 100f);

        Assert.True(remaining < 0f);
    }

    [Fact]
    public void GetTravelSlotMaxHoldSeconds_NullSettings_ReturnsBaseHold()
    {
        float result = TravelCooldown.GetTravelSlotMaxHoldSeconds(null);

        Assert.Equal(60f, result);
    }

    [Fact]
    public void GetTravelSlotMaxHoldSeconds_AddsTransitionDuration()
    {
        var settings = new TravelTransitionSettings { DurationSeconds = 5f };

        float result = TravelCooldown.GetTravelSlotMaxHoldSeconds(settings);

        Assert.Equal(65f, result);
    }

    [Fact]
    public void GetTravelSlotMaxHoldSeconds_NegativeDuration_ClampsToZero()
    {
        var settings = new TravelTransitionSettings { DurationSeconds = -5f };

        float result = TravelCooldown.GetTravelSlotMaxHoldSeconds(settings);

        Assert.Equal(60f, result);
    }
}
