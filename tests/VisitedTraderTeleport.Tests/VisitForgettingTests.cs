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

    // --- removing -------------------------------------------------------------------------

    [Fact]
    public void RemovesTheKeyFromThePlayersOwnVisits()
    {
        Dictionary<string, HashSet<string>> visits = Visits((Me, new[] { Trader, "other:1:2" }));

        Assert.True(VisitForgetting.TryRemoveVisit(visits, Me, Trader));

        Assert.DoesNotContain(Trader, visits[Me]);
        Assert.Contains("other:1:2", visits[Me]);
    }

    [Fact]
    public void NeverTouchesAnotherPlayersVisits()
    {
        Dictionary<string, HashSet<string>> visits = Visits(
            (Me, new[] { Trader }),
            (SomeoneElse, new[] { Trader }));

        VisitForgetting.TryRemoveVisit(visits, Me, Trader);

        Assert.Contains(Trader, visits[SomeoneElse]);
    }

    [Fact]
    public void RemovesNothingWhenThePlayerNeverVisitedIt()
    {
        Dictionary<string, HashSet<string>> visits = Visits((SomeoneElse, new[] { Trader }));

        Assert.False(VisitForgetting.TryRemoveVisit(visits, Me, Trader));

        Assert.Contains(Trader, visits[SomeoneElse]);
    }

    [Fact]
    public void RemovesNothingWhenTheKeyIsNotInThePlayersVisits()
    {
        Dictionary<string, HashSet<string>> visits = Visits((Me, new[] { "other:1:2" }));

        Assert.False(VisitForgetting.TryRemoveVisit(visits, Me, Trader));

        Assert.Contains("other:1:2", visits[Me]);
    }

    // An empty set and no set at all mean the same thing to every reader of this map, and one
    // of them gets written to the save on every load.
    [Fact]
    public void DropsThePlayerEntirelyWhenTheirLastVisitGoes()
    {
        Dictionary<string, HashSet<string>> visits = Visits((Me, new[] { Trader }));

        VisitForgetting.TryRemoveVisit(visits, Me, Trader);

        Assert.False(visits.ContainsKey(Me));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "key")]
    [InlineData("player", "")]
    public void RefusesIncompleteInput(string playerKey, string destinationKey)
    {
        Dictionary<string, HashSet<string>> visits = Visits((Me, new[] { Trader }));

        Assert.False(VisitForgetting.TryRemoveVisit(visits, playerKey, destinationKey));

        Assert.Contains(Trader, visits[Me]);
    }

    [Fact]
    public void SurvivesAMissingVisitMap()
    {
        Assert.False(VisitForgetting.TryRemoveVisit(null, Me, Trader));
    }

    // --- what the player is told ----------------------------------------------------------
    //
    // The whole truth table, because every wrong answer here is a sentence the player acts on.
    // The first version took one key set for both questions and consulted it before mutating -
    // and C# evaluates arguments before the call, so it was always the pre-removal list. Every
    // successful removal announced that something else was keeping the destination listed,
    // including in single player where there is nothing else. Four cases had collapsed into
    // one wrong one, and no test noticed because the set was passed in by hand.

    [Fact]
    public void RemovedAndGoneIsRemoved()
    {
        Assert.Equal(ForgetOutcome.Removed, VisitForgetting.Decide(removed: true, listedNow: false));
    }

    [Fact]
    public void RemovedButSomethingElseKeepsItListed()
    {
        Assert.Equal(
            ForgetOutcome.RemovedButStillListed,
            VisitForgetting.Decide(removed: true, listedNow: true));
    }

    [Fact]
    public void ListedButNotYoursMeansThereIsNothingToRemove()
    {
        Assert.Equal(
            ForgetOutcome.NothingOfTheirsToRemove,
            VisitForgetting.Decide(removed: false, listedNow: true));
    }

    // The stale-snapshot case: a client forgets twice before the new list reaches it.
    [Fact]
    public void NotRemovedAndNotListedMeansItIsAlreadyGone()
    {
        Assert.Equal(
            ForgetOutcome.NotOnTheirList,
            VisitForgetting.Decide(removed: false, listedNow: false));
    }

    // --- messages ---------------------------------------------------------------------------
    //
    // One [Fact] each rather than a [Theory] taking the outcome: ForgetOutcome is internal, and
    // xunit needs its test methods public, which makes an internal parameter type a compile
    // error (CS0051). A local of that type is fine, so the value moves inside the method.

    [Fact]
    public void EveryOutcomeHasItsOwnMessage()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal)
        {
            VisitForgetting.GetMessageKey(ForgetOutcome.Removed),
            VisitForgetting.GetMessageKey(ForgetOutcome.RemovedButStillListed),
            VisitForgetting.GetMessageKey(ForgetOutcome.NothingOfTheirsToRemove),
            VisitForgetting.GetMessageKey(ForgetOutcome.NotOnTheirList),
        };

        // Four outcomes, four distinct strings. Sharing one would put a sentence in front of
        // the player that describes a different situation, which is the bug this split exists
        // to prevent.
        Assert.Equal(4, keys.Count);
    }

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
    public void NothingOfTheirsHasItsOwnMessage()
    {
        Assert.Equal(
            "vtt_forget_not_yours",
            VisitForgetting.GetMessageKey(ForgetOutcome.NothingOfTheirsToRemove));
    }

    [Fact]
    public void NotOnTheirListHasItsOwnMessage()
    {
        Assert.Equal(
            "vtt_forget_not_listed",
            VisitForgetting.GetMessageKey(ForgetOutcome.NotOnTheirList));
    }
}
