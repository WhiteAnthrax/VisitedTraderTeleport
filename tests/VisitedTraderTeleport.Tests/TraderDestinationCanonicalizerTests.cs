using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class TraderDestinationCanonicalizerTests
{
    private sealed class FakeTraderAreaLookup : ITraderAreaLookup
    {
        private readonly Position3? matchingPosition;
        private readonly int areaX;
        private readonly int areaZ;
        private readonly int localX;
        private readonly int localZ;

        public FakeTraderAreaLookup(Position3? matchingPosition, int areaX, int areaZ, int localX, int localZ)
        {
            this.matchingPosition = matchingPosition;
            this.areaX = areaX;
            this.areaZ = areaZ;
            this.localX = localX;
            this.localZ = localZ;
        }

        public int CallCount { get; private set; }

        public bool TryFindTraderArea(Position3 position, out int areaX, out int areaZ, out int localX, out int localZ)
        {
            CallCount++;
            if (matchingPosition.HasValue && position.Equals(matchingPosition.Value))
            {
                areaX = this.areaX;
                areaZ = this.areaZ;
                localX = this.localX;
                localZ = this.localZ;
                return true;
            }

            areaX = 0;
            areaZ = 0;
            localX = 0;
            localZ = 0;
            return false;
        }
    }

    private static TraderDestination CreateDestination(string key, Position3 position)
    {
        return new TraderDestination
        {
            Key = key,
            DisplayName = "Trader Rekt",
            Position = position,
            Forward = Position3.Forward,
            AreaX = 0,
            AreaZ = 0
        };
    }

    [Fact]
    public void Canonicalize_NullDestination_ReturnsNull()
    {
        var lookup = new FakeTraderAreaLookup(null, 0, 0, 0, 0);

        TraderDestination result = TraderDestinationCanonicalizer.Canonicalize(null, lookup);

        Assert.Null(result);
    }

    [Fact]
    public void Canonicalize_AreaFoundAtDestinationPosition_BuildsCanonicalKey()
    {
        var position = new Position3(100f, 0f, 200f);
        var destination = CreateDestination("rekt", position);
        var lookup = new FakeTraderAreaLookup(position, areaX: 96, areaZ: 196, localX: 4, localZ: 4);

        TraderDestination result = TraderDestinationCanonicalizer.Canonicalize(destination, lookup);

        Assert.Equal("rekt:96:196:4:4", result.Key);
        Assert.Equal(96, result.AreaX);
        Assert.Equal(196, result.AreaZ);
    }

    [Fact]
    public void Canonicalize_AreaNotFoundAtIdentityPosition_FallsBackToDestinationPosition()
    {
        var identityPosition = new Position3(999f, 0f, 999f);
        var destinationPosition = new Position3(100f, 0f, 200f);
        var destination = CreateDestination("rekt", destinationPosition);
        var lookup = new FakeTraderAreaLookup(destinationPosition, areaX: 96, areaZ: 196, localX: 4, localZ: 4);

        TraderDestination result = TraderDestinationCanonicalizer.Canonicalize(destination, lookup, identityPosition);

        Assert.Equal(2, lookup.CallCount);
        Assert.Equal("rekt:96:196:4:4", result.Key);
    }

    [Fact]
    public void Canonicalize_AreaNeverFound_ReturnsOriginalDestinationUnchanged()
    {
        var position = new Position3(100f, 0f, 200f);
        var destination = CreateDestination("rekt", position);
        var lookup = new FakeTraderAreaLookup(null, 0, 0, 0, 0);

        TraderDestination result = TraderDestinationCanonicalizer.Canonicalize(destination, lookup);

        Assert.Same(destination, result);
    }

    [Fact]
    public void Canonicalize_KeyAlreadyCanonical_ReturnsSameInstance()
    {
        var position = new Position3(100f, 0f, 200f);
        var destination = new TraderDestination
        {
            Key = "trader:96:196:4:4",
            DisplayName = "Trader Rekt",
            Position = position,
            Forward = Position3.Forward,
            AreaX = 96,
            AreaZ = 196
        };
        var lookup = new FakeTraderAreaLookup(position, areaX: 96, areaZ: 196, localX: 4, localZ: 4);

        TraderDestination result = TraderDestinationCanonicalizer.Canonicalize(destination, lookup);

        Assert.Same(destination, result);
    }

    [Fact]
    public void Canonicalize_KeyPrefixEmpty_DefaultsToTraderPrefix()
    {
        var position = new Position3(100f, 0f, 200f);
        var destination = CreateDestination(string.Empty, position);
        var lookup = new FakeTraderAreaLookup(position, areaX: 96, areaZ: 196, localX: 4, localZ: 4);

        TraderDestination result = TraderDestinationCanonicalizer.Canonicalize(destination, lookup);

        Assert.StartsWith("trader:", result.Key);
    }
}
