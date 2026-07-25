using System.Collections.Generic;
using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

// VisitedTraderClientState holds static state, so every test starts by calling ApplySnapshot
// to establish a known baseline regardless of test execution order.
public class VisitedTraderClientStateTests
{
    [Fact]
    public void ApplySnapshot_SetsAccessModeAndConfirmation()
    {
        VisitedTraderClientState.ApplySnapshot(
            AccessMode.Party,
            new List<TraderDestination>(),
            confirmation: ConfirmationMode.Always);

        Assert.Equal(AccessMode.Party, VisitedTraderClientState.ServerAccessMode);
        Assert.Equal(ConfirmationMode.Always, VisitedTraderClientState.ServerConfirmation);
    }

    [Fact]
    public void ApplySnapshot_NullTravelCost_DefaultsToDisabled()
    {
        VisitedTraderClientState.ApplySnapshot(AccessMode.Personal, new List<TraderDestination>());

        Assert.False(VisitedTraderClientState.ServerTravelCost.Enabled);
    }

    [Fact]
    public void GetDestinations_OrdersByDisplayNameThenArea()
    {
        var destinations = new List<TraderDestination>
        {
            new() { Key = "b", DisplayName = "Bob", AreaX = 1, AreaZ = 1 },
            new() { Key = "a1", DisplayName = "Alice", AreaX = 5, AreaZ = 0 },
            new() { Key = "a0", DisplayName = "Alice", AreaX = 0, AreaZ = 0 }
        };

        VisitedTraderClientState.ApplySnapshot(AccessMode.Personal, destinations);
        IReadOnlyList<TraderDestination> result = VisitedTraderClientState.GetDestinations();

        Assert.Equal(new[] { "a0", "a1", "b" }, new[] { result[0].Key, result[1].Key, result[2].Key });
    }

    [Fact]
    public void TryGet_KnownKey_ReturnsDestination()
    {
        var destination = new TraderDestination { Key = "trader:1:2", DisplayName = "Trader Bob" };
        VisitedTraderClientState.ApplySnapshot(AccessMode.Personal, new[] { destination });

        bool found = VisitedTraderClientState.TryGet("trader:1:2", out TraderDestination result);

        Assert.True(found);
        Assert.Same(destination, result);
    }

    [Fact]
    public void TryGet_UnknownKey_ReturnsFalse()
    {
        VisitedTraderClientState.ApplySnapshot(AccessMode.Personal, new List<TraderDestination>());

        bool found = VisitedTraderClientState.TryGet("does-not-exist", out TraderDestination result);

        Assert.False(found);
        Assert.Null(result);
    }
}
