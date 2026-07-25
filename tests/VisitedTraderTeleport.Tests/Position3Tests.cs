using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class Position3Tests
{
    [Fact]
    public void Constructor_SetsComponents()
    {
        var position = new Position3(1f, 2f, 3f);

        Assert.Equal(1f, position.X);
        Assert.Equal(2f, position.Y);
        Assert.Equal(3f, position.Z);
    }

    [Fact]
    public void Zero_IsAllZeroComponents()
    {
        Assert.Equal(0f, Position3.Zero.X);
        Assert.Equal(0f, Position3.Zero.Y);
        Assert.Equal(0f, Position3.Zero.Z);
    }

    [Fact]
    public void Forward_IsUnitZ()
    {
        Assert.Equal(0f, Position3.Forward.X);
        Assert.Equal(0f, Position3.Forward.Y);
        Assert.Equal(1f, Position3.Forward.Z);
    }

    [Fact]
    public void Equals_SameComponents_ReturnsTrue()
    {
        var left = new Position3(1f, 2f, 3f);
        var right = new Position3(1f, 2f, 3f);

        Assert.True(left.Equals(right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Theory]
    [InlineData(9f, 2f, 3f)]
    [InlineData(1f, 9f, 3f)]
    [InlineData(1f, 2f, 9f)]
    public void Equals_DifferentComponent_ReturnsFalse(float x, float y, float z)
    {
        var left = new Position3(1f, 2f, 3f);
        var right = new Position3(x, y, z);

        Assert.False(left.Equals(right));
    }
}
