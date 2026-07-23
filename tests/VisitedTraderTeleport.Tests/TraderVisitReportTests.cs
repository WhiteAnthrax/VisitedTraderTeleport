using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class TraderVisitReportTests
{
    [Fact]
    public void HasTraderPosition_AllComponentsZero_ReturnsFalse()
    {
        var report = new TraderVisitReport
        {
            TraderPositionX = 0f,
            TraderPositionY = 0f,
            TraderPositionZ = 0f
        };

        Assert.False(report.HasTraderPosition);
    }

    [Theory]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0f, 1f, 0f)]
    [InlineData(0f, 0f, 1f)]
    [InlineData(-1f, 0f, 0f)]
    public void HasTraderPosition_AnyComponentNonZero_ReturnsTrue(float x, float y, float z)
    {
        var report = new TraderVisitReport
        {
            TraderPositionX = x,
            TraderPositionY = y,
            TraderPositionZ = z
        };

        Assert.True(report.HasTraderPosition);
    }
}
