namespace VisitedTraderTeleport;

// What happened when a player asked to forget a destination.
//
// Four answers rather than two, because the destination list is not only the player's own
// visits: Party and Shared mode add other players' visits, and a save carried over from
// before 0.4.16 adds entries from the legacy TXT that belong to nobody. "Nothing was removed"
// therefore has more than one cause, and they are not interchangeable - a message that names
// the wrong one is worse than a vague one, because the player acts on it.
internal enum ForgetOutcome
{
    // The record was removed and the destination is gone from this player's list.
    Removed,

    // The record was removed, but the destination is still listed: another player has visited
    // that trader in an access mode that shares visits, or it comes from the legacy file.
    // Nothing more this player can do - the alternative would be deleting someone else's
    // record.
    RemovedButStillListed,

    // The destination is on this player's list, but not because of anything they did, so
    // there is nothing of theirs to remove.
    NothingOfTheirsToRemove,

    // The destination is not on this player's list at all. Reachable on a client, whose list
    // is a snapshot: forgetting twice before the new snapshot arrives asks the server to
    // remove something that has already gone. Telling that player "another player has visited
    // this trader" - as this used to - is simply false.
    NotOnTheirList,
}
