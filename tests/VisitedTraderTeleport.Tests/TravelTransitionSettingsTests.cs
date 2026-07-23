using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class TravelTransitionSettingsTests
{
    [Fact]
    public void Default_ReturnsEnabledSettingsWithNonZeroDuration()
    {
        TravelTransitionSettings settings = TravelTransitionSettings.Default();

        Assert.True(settings.Enabled);
        Assert.True(settings.DurationSeconds > 0f);
    }

    [Fact]
    public void Disabled_ReturnsSettingsWithEnabledFalseAndZeroedFields()
    {
        TravelTransitionSettings settings = TravelTransitionSettings.Disabled();

        Assert.False(settings.Enabled);
        Assert.Equal(0f, settings.DurationSeconds);
        Assert.Equal(string.Empty, settings.Sound);
        Assert.Equal(0f, settings.SoundRepeatSeconds);
    }

    [Fact]
    public void Clone_MutatingClone_DoesNotAffectOriginal()
    {
        TravelTransitionSettings original = TravelTransitionSettings.Default();

        TravelTransitionSettings clone = original.Clone();
        clone.Enabled = false;
        clone.DurationSeconds = 0f;
        clone.Sound = "changed";

        Assert.True(original.Enabled);
        Assert.NotEqual(0f, original.DurationSeconds);
        Assert.NotEqual("changed", original.Sound);
    }
}
