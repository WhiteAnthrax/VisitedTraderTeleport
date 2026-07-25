using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class TraderRecordConverterTests
{
    [Fact]
    public void ToRecord_FromRecord_RoundTripsAllFields()
    {
        var original = new TraderDestination
        {
            Key = "rekt:100:200",
            DisplayName = "Trader Rekt",
            Position = new Position3(100.5f, 64f, 200.25f),
            Forward = new Position3(0f, 0f, -1f),
            AreaX = 100,
            AreaZ = 200,
            Biome = "forest"
        };

        TraderDestinationRecord record = TraderRecordConverter.ToRecord(original);
        TraderDestination result = TraderRecordConverter.FromRecord(record, "fallback");

        Assert.Equal(original.Key, result.Key);
        Assert.Equal(original.DisplayName, result.DisplayName);
        Assert.Equal(original.Position, result.Position);
        Assert.Equal(original.Forward, result.Forward);
        Assert.Equal(original.AreaX, result.AreaX);
        Assert.Equal(original.AreaZ, result.AreaZ);
        Assert.Equal(original.Biome, result.Biome);
    }

    [Fact]
    public void FromRecord_NullRecord_ReturnsNull()
    {
        Assert.Null(TraderRecordConverter.FromRecord(null, "fallback"));
    }

    [Fact]
    public void FromRecord_EmptyKeyInRecord_UsesFallbackKey()
    {
        var record = new TraderDestinationRecord { Key = string.Empty, DisplayName = "Trader Bob" };

        TraderDestination result = TraderRecordConverter.FromRecord(record, "fallback-key");

        Assert.Equal("fallback-key", result.Key);
    }

    [Fact]
    public void RecordsEqual_IdenticalRecords_ReturnsTrue()
    {
        var left = new TraderDestinationRecord
        {
            Key = "rekt:100:200",
            DisplayName = "Rekt",
            PositionX = 1f,
            PositionY = 2f,
            PositionZ = 3f,
            ForwardX = 0f,
            ForwardY = 0f,
            ForwardZ = 1f,
            AreaX = 100,
            AreaZ = 200,
            Biome = "forest"
        };
        var right = new TraderDestinationRecord
        {
            Key = "rekt:100:200",
            DisplayName = "Rekt",
            PositionX = 1f,
            PositionY = 2f,
            PositionZ = 3f,
            ForwardX = 0f,
            ForwardY = 0f,
            ForwardZ = 1f,
            AreaX = 100,
            AreaZ = 200,
            Biome = "forest"
        };

        Assert.True(TraderRecordConverter.RecordsEqual(left, right));
    }

    [Fact]
    public void RecordsEqual_DifferentPosition_ReturnsFalse()
    {
        var left = new TraderDestinationRecord { Key = "rekt", PositionX = 1f };
        var right = new TraderDestinationRecord { Key = "rekt", PositionX = 2f };

        Assert.False(TraderRecordConverter.RecordsEqual(left, right));
    }

    [Fact]
    public void RecordsEqual_OneNull_ReturnsFalse()
    {
        var left = new TraderDestinationRecord { Key = "rekt" };

        Assert.False(TraderRecordConverter.RecordsEqual(left, null));
        Assert.False(TraderRecordConverter.RecordsEqual(null, left));
    }

    [Fact]
    public void RecordsEqual_BothNull_ReturnsTrue()
    {
        Assert.True(TraderRecordConverter.RecordsEqual(null, null));
    }

    [Fact]
    public void WithKey_ReplacesKeyOnly()
    {
        var original = new TraderDestination
        {
            Key = "old-key",
            DisplayName = "Rekt",
            Position = new Position3(1f, 2f, 3f),
            Forward = Position3.Forward,
            AreaX = 100,
            AreaZ = 200,
            Biome = "forest"
        };

        TraderDestination result = TraderRecordConverter.WithKey(original, "new-key");

        Assert.Equal("new-key", result.Key);
        Assert.Equal(original.DisplayName, result.DisplayName);
        Assert.Equal(original.Position, result.Position);
        Assert.Equal(original.Forward, result.Forward);
        Assert.Equal(original.AreaX, result.AreaX);
        Assert.Equal(original.AreaZ, result.AreaZ);
        Assert.Equal(original.Biome, result.Biome);
    }
}
