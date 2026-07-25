using System.Collections.Generic;
using System.Linq;
using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class TravelSoundCandidatesTests
{
    [Fact]
    public void GetCandidates_PlainName_ReturnsNameThenBracketedVariant()
    {
        List<string> candidates = TravelSoundCandidates.GetCandidates("suv_startup").ToList();

        Assert.Equal(new[] { "suv_startup", "[suv_startup]" }, candidates);
    }

    [Fact]
    public void GetCandidates_BracketedName_ReturnsNameThenUnbracketedVariant()
    {
        List<string> candidates = TravelSoundCandidates.GetCandidates("[suv_startup]").ToList();

        Assert.Equal(new[] { "[suv_startup]", "suv_startup" }, candidates);
    }

    [Fact]
    public void GetCandidates_EmptyString_ReturnsBracketedVariant()
    {
        List<string> candidates = TravelSoundCandidates.GetCandidates(string.Empty).ToList();

        Assert.Equal(new[] { string.Empty, "[]" }, candidates);
    }
}
