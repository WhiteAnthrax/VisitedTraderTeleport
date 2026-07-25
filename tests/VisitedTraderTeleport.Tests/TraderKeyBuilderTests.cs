using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class TraderKeyBuilderTests
{
    [Theory]
    [InlineData("rekt:100:200", "rekt")]
    [InlineData("rekt", "rekt")]
    [InlineData("REKT:100:200", "rekt")]
    [InlineData(" rekt :100:200", "rekt")]
    public void GetKeyPrefix_ExtractsAndNormalizesPrefix(string key, string expected)
    {
        string result = TraderKeyBuilder.GetKeyPrefix(key);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetKeyPrefix_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TraderKeyBuilder.GetKeyPrefix(null));
        Assert.Equal(string.Empty, TraderKeyBuilder.GetKeyPrefix("   "));
    }

    [Fact]
    public void BuildCanonicalKey_FormatsAllComponents()
    {
        string key = TraderKeyBuilder.BuildCanonicalKey("rekt", 100, -200, 4, -8);

        Assert.Equal("rekt:100:-200:4:-8", key);
    }
}
