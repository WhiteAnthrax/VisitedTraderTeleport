using System.Collections.Generic;
using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class TraderNameFormatterTests
{
    // Behaves like the game's Localization.Get: a missing key comes back unchanged.
    private sealed class DictionaryLocalizationProvider : ILocalizationProvider
    {
        private readonly Dictionary<string, string> entries;

        public DictionaryLocalizationProvider(Dictionary<string, string> entries)
        {
            this.entries = entries;
        }

        public string Get(string key) => entries.TryGetValue(key, out string value) ? value : key;

        public string Format(string key, params object[] args) => string.Format(Get(key), args);
    }

    private static DictionaryLocalizationProvider Localization(params (string Key, string Value)[] gameEntries)
    {
        var entries = new Dictionary<string, string>
        {
            ["vtt_trader_name_rekt"] = "レクト",
            ["vtt_trader_name_joel"] = "ジョエル",
            ["vtt_trader_name_gene"] = "ジーン",
            ["vtt_trader_name_generic"] = "トレーダー",
            ["vtt_trader_name_unknown"] = "トレーダー {0}"
        };
        foreach ((string key, string value) in gameEntries)
        {
            entries[key] = value;
        }

        return new DictionaryLocalizationProvider(entries);
    }

    [Theory]
    [InlineData("トレーダー・レクト", "レクト")]
    [InlineData("Rekt le marchand", "Rekt")]
    [InlineData("Trader rekt", "Rekt")]
    public void Format_KeepsShortName_WhenGameNameStillContainsIt(string gameName, string shortName)
    {
        var localization = Localization(("npcTraderRekt", gameName), ("vtt_trader_name_rekt", shortName));

        Assert.Equal(shortName, TraderNameFormatter.Format("npcTraderRekt", localization));
    }

    [Fact]
    public void Format_UsesGameName_WhenAnOverhaulRenamesAKnownTrader()
    {
        var localization = Localization(("npcTraderRekt", "イゴン"), ("npcTraderJoel", "ペスティレンス博士"));

        Assert.Equal("イゴン", TraderNameFormatter.Format("npcTraderRekt", localization));
        Assert.Equal("ペスティレンス博士", TraderNameFormatter.Format("npcTraderJoel", localization));
    }

    [Fact]
    public void Format_KeepsShortName_WhenTheGameHasNoEntryForTheName()
    {
        var localization = Localization();

        Assert.Equal("レクト", TraderNameFormatter.Format("npcTraderRekt", localization));
        Assert.Equal("ジーン", TraderNameFormatter.Format("Gene", localization));
    }

    [Fact]
    public void Format_UsesGameName_ForAnUnknownTraderWithAnEntry()
    {
        var localization = Localization(("npcTraderLea", "リー・マーシュ"));

        Assert.Equal("リー・マーシュ", TraderNameFormatter.Format("npcTraderLea", localization));
    }

    [Fact]
    public void Format_FallsBackToTitledName_ForAnUnknownTraderWithoutAnEntry()
    {
        var localization = Localization();

        Assert.Equal("トレーダー Lea Marsh", TraderNameFormatter.Format("npcTraderLeaMarsh", localization));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Format_UsesGenericName_WhenNameIsMissing(string rawName)
    {
        Assert.Equal("トレーダー", TraderNameFormatter.Format(rawName, Localization()));
    }

    [Fact]
    public void Format_IgnoresSurroundingWhitespace_WhenLookingUpTheGameName()
    {
        var localization = Localization(("npcTraderRekt", "イゴン"));

        Assert.Equal("イゴン", TraderNameFormatter.Format(" npcTraderRekt ", localization));
    }
}
