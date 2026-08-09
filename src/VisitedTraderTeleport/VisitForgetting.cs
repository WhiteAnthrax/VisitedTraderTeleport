using System.Collections.Generic;

namespace VisitedTraderTeleport;

// Removing one player's visit record, and working out what that player will see afterwards.
//
// Kept apart from VisitedTraderStore so it can be tested: the store talks to the game and to
// the save file, and the part worth being sure about is this - which set is touched, and what
// the player is told when the destination stays on their list anyway.
//
// The rule this encodes, and it is the whole safety argument for the feature: a player can
// only ever remove *their own* visit. Another player's record is never touched, in any access
// mode. Someone clearing out a trader they spawned for testing cannot take a destination away
// from anybody else.
internal static class VisitForgetting
{
    // visitsByPlayer is the store's own map and is modified in place.
    // visibleKeysAfterRemoval is what the player would be able to see once the removal has
    // happened - the store computes it with the same access-mode logic the dialog uses, so
    // this cannot disagree with the list the player is looking at.
    public static ForgetOutcome Forget(
        IDictionary<string, HashSet<string>> visitsByPlayer,
        string playerKey,
        string destinationKey,
        ICollection<string> visibleKeysAfterRemoval)
    {
        if (visitsByPlayer == null ||
            string.IsNullOrEmpty(playerKey) ||
            string.IsNullOrEmpty(destinationKey) ||
            !visitsByPlayer.TryGetValue(playerKey, out HashSet<string> ownVisits) ||
            ownVisits == null ||
            !ownVisits.Remove(destinationKey))
        {
            return ForgetOutcome.NotVisitedByThisPlayer;
        }

        // An empty set and no set at all mean the same thing everywhere else, so do not leave
        // one behind - it would be written to the save on every load from here on.
        if (ownVisits.Count == 0)
        {
            visitsByPlayer.Remove(playerKey);
        }

        return visibleKeysAfterRemoval != null && visibleKeysAfterRemoval.Contains(destinationKey)
            ? ForgetOutcome.RemovedButStillListed
            : ForgetOutcome.Removed;
    }

    // The message key for each outcome, so the dialog and any other caller say the same thing.
    public static string GetMessageKey(ForgetOutcome outcome)
    {
        switch (outcome)
        {
            case ForgetOutcome.Removed:
                return "vtt_forget_done";
            case ForgetOutcome.RemovedButStillListed:
                return "vtt_forget_still_listed";
            default:
                return "vtt_forget_not_yours";
        }
    }
}
