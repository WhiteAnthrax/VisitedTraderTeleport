using System.Collections.Generic;
using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class TraderMatchingTests
{
    private static TraderDestination CreateDestination(
        string key, string displayName, float x, float z, int areaX = 0, int areaZ = 0)
    {
        return new TraderDestination
        {
            Key = key,
            DisplayName = displayName,
            Position = new Position3(x, 0f, z),
            Forward = Position3.Forward,
            AreaX = areaX,
            AreaZ = areaZ
        };
    }

    [Fact]
    public void IsSameTrader_SameKey_ReturnsTrue()
    {
        var a = CreateDestination("rekt:100:200", "Rekt", 100f, 200f);
        var b = CreateDestination("rekt:100:200", "Rekt", 105f, 205f);

        Assert.True(TraderMatching.IsSameTrader(a, b));
    }

    [Fact]
    public void IsSameTrader_NullArgument_ReturnsFalse()
    {
        var a = CreateDestination("rekt:100:200", "Rekt", 100f, 200f);

        Assert.False(TraderMatching.IsSameTrader(null, a));
        Assert.False(TraderMatching.IsSameTrader(a, null));
    }

    [Fact]
    public void IsSameTrader_DifferentKeyButNearbyPosition_ReturnsTrue()
    {
        // Different area (avoids the same-area/same-name shortcut) but close enough (5m) to
        // fall within the default tolerance, with compatible identity via matching key prefix.
        var a = CreateDestination("rekt:100:200", "Trader Rekt", 100f, 200f, areaX: 100, areaZ: 200);
        var b = CreateDestination("rekt:100:200:0:0", "npcTraderRekt", 105f, 200f, areaX: 999, areaZ: 999);

        Assert.True(TraderMatching.IsSameTrader(a, b));
    }

    [Fact]
    public void IsSameTrader_FarApart_ReturnsFalse()
    {
        var a = CreateDestination("rekt:100:200", "Trader Rekt", 100f, 200f, areaX: 100, areaZ: 200);
        var b = CreateDestination("rekt:900:900", "Trader Rekt", 900f, 900f, areaX: 900, areaZ: 900);

        Assert.False(TraderMatching.IsSameTrader(a, b));
    }

    [Fact]
    public void IsSameTrader_IncompatibleIdentity_ReturnsFalse()
    {
        var a = CreateDestination("rekt:100:200", "Trader Rekt", 100f, 200f);
        var b = CreateDestination("bob:100:200", "Trader Bob", 100f, 200f);

        Assert.False(TraderMatching.IsSameTrader(a, b));
    }

    [Fact]
    public void DeduplicateDestinations_PrefersMoreSpecificKey()
    {
        var coarse = CreateDestination("rekt:100:200", "Trader Rekt", 100f, 200f);
        var detailed = CreateDestination("rekt:100:200:4:8", "Trader Rekt", 101f, 201f);

        List<TraderDestination> result = TraderMatching.DeduplicateDestinations(new[] { coarse, detailed });

        Assert.Single(result);
        Assert.Equal("rekt:100:200:4:8", result[0].Key);
    }

    [Fact]
    public void DeduplicateDestinations_DistinctTraders_KeepsBoth()
    {
        var rekt = CreateDestination("rekt:100:200", "Trader Rekt", 100f, 200f, areaX: 100, areaZ: 200);
        var bob = CreateDestination("bob:900:900", "Trader Bob", 900f, 900f, areaX: 900, areaZ: 900);

        List<TraderDestination> result = TraderMatching.DeduplicateDestinations(new[] { rekt, bob });

        Assert.Equal(2, result.Count);
    }
}
