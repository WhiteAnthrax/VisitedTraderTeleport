using Xunit;

namespace VisitedTraderTeleport.Tests;

public class CompanionIdentificationTests
{
    private const int PlayerId = 171;
    private const int OtherPlayerId = 999;

    [Fact]
    public void HiredCompanionWithLeaderVar_IsCompanion()
    {
        Assert.True(CompanionIdentification.IsCompanion(
            isExcludedType: false,
            hasLeaderVar: true, leaderValue: PlayerId,
            hasOwnerVar: false, ownerValue: 0,
            playerId: PlayerId));
    }

    [Fact]
    public void HiredCompanionWithOwnerVar_IsCompanion()
    {
        Assert.True(CompanionIdentification.IsCompanion(
            isExcludedType: false,
            hasLeaderVar: false, leaderValue: 0,
            hasOwnerVar: true, ownerValue: PlayerId,
            playerId: PlayerId));
    }

    // The bug this file exists for: a placed turret sets belongsPlayerId to its owner, which
    // the old ownership check read as "companion". It carries no Leader/Owner Buffs var, so
    // with those as the only signal it is correctly left alone - even before the type check.
    [Fact]
    public void OwnedEntityWithoutLeaderOrOwnerVar_IsNotCompanion()
    {
        Assert.False(CompanionIdentification.IsCompanion(
            isExcludedType: false,
            hasLeaderVar: false, leaderValue: 0,
            hasOwnerVar: false, ownerValue: 0,
            playerId: PlayerId));
    }

    [Fact]
    public void AnotherPlayersCompanion_IsNotCompanion()
    {
        Assert.False(CompanionIdentification.IsCompanion(
            isExcludedType: false,
            hasLeaderVar: true, leaderValue: OtherPlayerId,
            hasOwnerVar: true, ownerValue: OtherPlayerId,
            playerId: PlayerId));
    }

    [Fact]
    public void ExcludedType_IsNotCompanion_EvenWhenMarkedAsHired()
    {
        Assert.False(CompanionIdentification.IsCompanion(
            isExcludedType: true,
            hasLeaderVar: true, leaderValue: PlayerId,
            hasOwnerVar: true, ownerValue: PlayerId,
            playerId: PlayerId));
    }

    // SCore's GetLeaderOrOwner tries Leader before Owner, so a companion whose Leader is this
    // player counts even if some other id is left in Owner.
    [Fact]
    public void LeaderTakesPrecedenceOverOwner()
    {
        Assert.True(CompanionIdentification.IsCompanion(
            isExcludedType: false,
            hasLeaderVar: true, leaderValue: PlayerId,
            hasOwnerVar: true, ownerValue: OtherPlayerId,
            playerId: PlayerId));
    }

    [Fact]
    public void OwnerMatchesWhenLeaderBelongsToSomeoneElse()
    {
        Assert.True(CompanionIdentification.IsCompanion(
            isExcludedType: false,
            hasLeaderVar: true, leaderValue: OtherPlayerId,
            hasOwnerVar: true, ownerValue: PlayerId,
            playerId: PlayerId));
    }
}
