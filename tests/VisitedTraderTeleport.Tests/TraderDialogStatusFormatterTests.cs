using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class TraderDialogStatusFormatterTests
{
    private sealed class FakeLocalizationProvider : ILocalizationProvider
    {
        public string Get(string key) => key;

        public string Format(string key, params object[] args) => $"{key}:{string.Join(",", args)}";
    }

    [Theory]
    [InlineData(AccessMode.Personal, "vtt_mode_personal_name")]
    [InlineData(AccessMode.Party, "vtt_mode_party_name")]
    [InlineData(AccessMode.Shared, "vtt_mode_shared_name")]
    public void FormatModeName_ReturnsModeSpecificKey(AccessMode accessMode, string expectedKey)
    {
        string result = TraderDialogStatusFormatter.FormatModeName(accessMode, new FakeLocalizationProvider());

        Assert.Equal(expectedKey, result);
    }

    [Theory]
    [InlineData(AccessMode.Personal, "vtt_mode_personal_name", "vtt_mode_personal_description")]
    [InlineData(AccessMode.Party, "vtt_mode_party_name", "vtt_mode_party_description")]
    [InlineData(AccessMode.Shared, "vtt_mode_shared_name", "vtt_mode_shared_description")]
    public void FormatModeLine_FormatsModeLineWithNameAndDescription(
        AccessMode accessMode, string expectedName, string expectedDescription)
    {
        string result = TraderDialogStatusFormatter.FormatModeLine(accessMode, new FakeLocalizationProvider());

        Assert.Equal($"vtt_mode_line:{expectedName},{expectedDescription}", result);
    }
}
