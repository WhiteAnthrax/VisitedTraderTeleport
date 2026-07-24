using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class TraderDestinationTests
{
    [Fact]
    public void Serialize_TryParse_RoundTripsAllFields()
    {
        var original = new TraderDestination
        {
            Key = "trader:100:200",
            DisplayName = "Trader Rekt",
            Position = new Position3(100.5f, 64f, 200.25f),
            Forward = new Position3(0f, 0f, -1f),
            AreaX = 100,
            AreaZ = 200
        };

        bool parsed = TraderDestination.TryParse(original.Serialize(), out TraderDestination result);

        Assert.True(parsed);
        Assert.Equal(original.Key, result.Key);
        Assert.Equal(original.DisplayName, result.DisplayName);
        Assert.Equal(original.Position, result.Position);
        Assert.Equal(original.Forward, result.Forward);
        Assert.Equal(original.AreaX, result.AreaX);
        Assert.Equal(original.AreaZ, result.AreaZ);
    }

    [Fact]
    public void Serialize_TryParse_RoundTripsDisplayNameWithEscapedCharacters()
    {
        var original = new TraderDestination
        {
            Key = "trader:1:2",
            DisplayName = "Trader | \"Bob\" \\ Rekt",
            Position = new Position3(1f, 2f, 3f),
            Forward = Position3.Forward,
            AreaX = 1,
            AreaZ = 2
        };

        bool parsed = TraderDestination.TryParse(original.Serialize(), out TraderDestination result);

        Assert.True(parsed);
        Assert.Equal(original.DisplayName, result.DisplayName);
    }

    [Fact]
    public void TryParse_EmptyLine_ReturnsFalse()
    {
        bool parsed = TraderDestination.TryParse(string.Empty, out TraderDestination result);

        Assert.False(parsed);
        Assert.Null(result);
    }

    [Fact]
    public void TryParse_CommentLine_ReturnsFalse()
    {
        bool parsed = TraderDestination.TryParse("# a comment", out TraderDestination result);

        Assert.False(parsed);
        Assert.Null(result);
    }

    [Fact]
    public void DialogText_RoundsPositionAwayFromZero()
    {
        var destination = new TraderDestination
        {
            DisplayName = "Trader Bob",
            Position = new Position3(10.5f, 0f, -5.5f)
        };

        Assert.Equal("Trader Bob (11, -6)", destination.DialogText);
    }
}
