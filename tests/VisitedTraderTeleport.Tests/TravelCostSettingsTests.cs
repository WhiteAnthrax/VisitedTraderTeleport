using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class TravelCostSettingsTests
{
    [Fact]
    public void Disabled_ReturnsSettingsWithEnabledFalse()
    {
        TravelCostSettings settings = TravelCostSettings.Disabled();

        Assert.False(settings.Enabled);
    }

    [Fact]
    public void Clone_CopiesAllFieldValues()
    {
        var original = new TravelCostSettings
        {
            Enabled = true,
            ItemName = "casinoCoin",
            ItemDisplayName = "Casino Coin",
            PerMeter = 0.25f,
            Minimum = 5
        };

        TravelCostSettings clone = original.Clone();

        Assert.Equal(original.Enabled, clone.Enabled);
        Assert.Equal(original.ItemName, clone.ItemName);
        Assert.Equal(original.ItemDisplayName, clone.ItemDisplayName);
        Assert.Equal(original.PerMeter, clone.PerMeter);
        Assert.Equal(original.Minimum, clone.Minimum);
    }

    [Fact]
    public void Clone_MutatingClone_DoesNotAffectOriginal()
    {
        var original = new TravelCostSettings { ItemName = "ammoGasCan", Minimum = 1 };

        TravelCostSettings clone = original.Clone();
        clone.ItemName = "casinoCoin";
        clone.Minimum = 99;

        Assert.Equal("ammoGasCan", original.ItemName);
        Assert.Equal(1, original.Minimum);
    }
}
