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
//
// Deliberately two calls rather than one. The first version took the visible key set as an
// argument and consulted it again after mutating - but C# evaluates arguments before the
// call, so that set was always the *pre-removal* one and still held the key. Every successful
// removal then reported itself as "removed, but it is still listed", including in single
// player where there is nobody else to keep it listed. Splitting the mutation from the
// verdict forces the caller to name the "after" set explicitly, so the same mistake cannot be
// made quietly again.
internal static class VisitForgetting
{
    // Removes this player's visit, if they had one, and says whether it removed anything.
    // visitsByPlayer is the store's own map and is modified in place.
    public static bool TryRemoveVisit(
        IDictionary<string, HashSet<string>> visitsByPlayer,
        string playerKey,
        string destinationKey)
    {
        if (visitsByPlayer == null ||
            string.IsNullOrEmpty(playerKey) ||
            string.IsNullOrEmpty(destinationKey) ||
            !visitsByPlayer.TryGetValue(playerKey, out HashSet<string> ownVisits) ||
            ownVisits == null ||
            !ownVisits.Remove(destinationKey))
        {
            return false;
        }

        // An empty set and no set at all mean the same thing everywhere else, so do not leave
        // one behind - it would be written to the save on every load from here on.
        if (ownVisits.Count == 0)
        {
            visitsByPlayer.Remove(playerKey);
        }

        return true;
    }

    // What to tell the player. listedNow is whether the destination is on their list *as
    // things stand after the removal* - the caller works that out, because only the caller
    // knows how a list is built: the access mode decides whose visits count, and a save from
    // before 0.4.16 contributes legacy entries that belong to nobody.
    public static ForgetOutcome Decide(bool removed, bool listedNow)
    {
        if (removed)
        {
            return listedNow ? ForgetOutcome.RemovedButStillListed : ForgetOutcome.Removed;
        }

        return listedNow ? ForgetOutcome.NothingOfTheirsToRemove : ForgetOutcome.NotOnTheirList;
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
            case ForgetOutcome.NothingOfTheirsToRemove:
                return "vtt_forget_not_yours";
            default:
                return "vtt_forget_not_listed";
        }
    }
}
