using System;
using System.Collections.Generic;
using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class VisitForgettingTests
{
    private const string Me = "EOS_me";
    private const string SomeoneElse = "EOS_them";
    private const string Trader = "traderjoel:478:1093";

    private static Dictionary<string, HashSet<string>> Visits(
        params (string player, string[] keys)[] entries)
    {
        var visits = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach ((string player, string[] keys) in entries)
        {
            visits[player] = new HashSet<string>(keys, StringComparer.Ordinal);
        }

        return visits;
    }

    private static HashSet<string> Visible(params string[] keys)
    {
        return new HashSet<string>(keys, StringComparer.Ordinal);
    }

    [Fact]
    public void RemovesTheKeyFromThePlayersOwnVisits()
    {
        Dictionary<string, HashSet<string>> visits = Visits((Me, new[] { Trader, "other:1:2" }));

        ForgetOutcome outcome = VisitForgetting.Forget(visits, Me, Trader, Visible("other:1:2"));

        Assert.Equal(ForgetOutcome.Removed, outcome);
        Assert.DoesNotContain(Trader, visits[Me]);
        Assert.Contains("other:1:2", visits[Me]);
    }

    [Fact]
    public void NeverTouchesAnotherPlayersVisits()
    {
        Dictionary<string, HashSet<string>> visits = Visits(
            (Me, new[] { Trader }),
            (SomeoneElse, new[] { Trader }));

        VisitForgetting.Forget(visits, Me, Trader, Visible(Trader));

        Assert.Contains(Trader, visits[SomeoneElse]);
    }

    // Shared and Party show the union of several players' visits, so removing your own record
    // does not always take the entry off your screen. The player has to be told that, or the
    // button looks broken.
    [Fact]
    public void SaysSoWhenSomeoneElsesVisitKeepsItListed()
    {
        Dictionary<string, HashSet<string>> visits = Visits(
            (Me, new[] { Trader }),
            (SomeoneElse, new[] { Trader }));

        ForgetOutcome outcome = VisitForgetting.Forget(visits, Me, Trader, Visible(Trader));

        Assert.Equal(ForgetOutcome.RemovedButStillListed, outcome);
        Assert.False(visits.ContainsKey(Me));
    }

    [Fact]
    public void ReportsWhenThePlayerNeverVisitedIt()
    {
        Dictionary<string, HashSet<string>> visits = Visits((SomeoneElse, new[] { Trader }));

        ForgetOutcome outcome = VisitForgetting.Forget(visits, Me, Trader, Visible(Trader));

        Assert.Equal(ForgetOutcome.NotVisitedByThisPlayer, outcome);
        Assert.Contains(Trader, visits[SomeoneElse]);
    }

    [Fact]
    public void ReportsWhenTheKeyIsNotInThePlayersVisits()
    {
        Dictionary<string, HashSet<string>> visits = Visits((Me, new[] { "other:1:2" }));

        ForgetOutcome outcome = VisitForgetting.Forget(visits, Me, Trader, Visible());

        Assert.Equal(ForgetOutcome.NotVisitedByThisPlayer, outcome);
        Assert.Contains("other:1:2", visits[Me]);
    }

    // An empty set and no set at all mean the same thing to every reader of this map, and one
    // of them gets written to the save on every load.
    [Fact]
    public void DropsThePlayerEntirelyWhenTheirLastVisitGoes()
    {
        Dictionary<string, HashSet<string>> visits = Visits((Me, new[] { Trader }));

        VisitForgetting.Forget(visits, Me, Trader, Visible());

        Assert.False(visits.ContainsKey(Me));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "key")]
    [InlineData("player", "")]
    public void RefusesIncompleteInput(string playerKey, string destinationKey)
    {
        Dictionary<string, HashSet<string>> visits = Visits((Me, new[] { Trader }));

        ForgetOutcome outcome = VisitForgetting.Forget(visits, playerKey, destinationKey, Visible());

        Assert.Equal(ForgetOutcome.NotVisitedByThisPlayer, outcome);
        Assert.Contains(Trader, visits[Me]);
    }

    [Fact]
    public void SurvivesAMissingVisitMap()
    {
        ForgetOutcome outcome = VisitForgetting.Forget(null, Me, Trader, Visible());

        Assert.Equal(ForgetOutcome.NotVisitedByThisPlayer, outcome);
    }

    // One [Fact] each rather than a [Theory] taking the outcome: ForgetOutcome is internal, and
    // xunit needs its test methods public, which makes an internal parameter type a compile
    // error (CS0051). A local of that type is fine, so the value moves inside the method.
    [Fact]
    public void RemovedHasItsOwnMessage()
    {
        Assert.Equal("vtt_forget_done", VisitForgetting.GetMessageKey(ForgetOutcome.Removed));
    }

    [Fact]
    public void StillListedHasItsOwnMessage()
    {
        Assert.Equal(
            "vtt_forget_still_listed",
            VisitForgetting.GetMessageKey(ForgetOutcome.RemovedButStillListed));
    }

    [Fact]
    public void NotVisitedHasItsOwnMessage()
    {
        Assert.Equal(
            "vtt_forget_not_yours",
            VisitForgetting.GetMessageKey(ForgetOutcome.NotVisitedByThisPlayer));
    }
}
