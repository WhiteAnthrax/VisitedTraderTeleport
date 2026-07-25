using System;
using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

// AccessMode is internal, so public [Theory] methods pass its name (as EnumDefinitionsTests
// does) and parse it back, rather than taking AccessMode itself as a public parameter type.
public class TraderDialogStatusFormatterTests
{
    private sealed class FakeLocalizationProvider : ILocalizationProvider
    {
        public string Get(string key) => key;

        public string Format(string key, params object[] args) => $"{key}:{string.Join(",", args)}";
    }

    [Theory]
    [InlineData(nameof(AccessMode.Personal), "vtt_mode_personal_name")]
    [InlineData(nameof(AccessMode.Party), "vtt_mode_party_name")]
    [InlineData(nameof(AccessMode.Shared), "vtt_mode_shared_name")]
    public void FormatModeName_ReturnsModeSpecificKey(string accessModeName, string expectedKey)
    {
        var accessMode = (AccessMode)Enum.Parse(typeof(AccessMode), accessModeName);

        string result = TraderDialogStatusFormatter.FormatModeName(accessMode, new FakeLocalizationProvider());

        Assert.Equal(expectedKey, result);
    }

    [Theory]
    [InlineData(nameof(AccessMode.Personal), "vtt_mode_personal_name", "vtt_mode_personal_description")]
    [InlineData(nameof(AccessMode.Party), "vtt_mode_party_name", "vtt_mode_party_description")]
    [InlineData(nameof(AccessMode.Shared), "vtt_mode_shared_name", "vtt_mode_shared_description")]
    public void FormatModeLine_FormatsModeLineWithNameAndDescription(
        string accessModeName, string expectedName, string expectedDescription)
    {
        var accessMode = (AccessMode)Enum.Parse(typeof(AccessMode), accessModeName);

        string result = TraderDialogStatusFormatter.FormatModeLine(accessMode, new FakeLocalizationProvider());

        Assert.Equal($"vtt_mode_line:{expectedName},{expectedDescription}", result);
    }
}
