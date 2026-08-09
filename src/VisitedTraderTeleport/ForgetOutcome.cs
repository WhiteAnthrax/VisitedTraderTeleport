namespace VisitedTraderTeleport;

// What happened when a player asked to forget a destination.
//
// There are three answers rather than two because the destination list is not always the
// player's own: in Party and Shared mode it is the union of several players' visits, so
// removing your own record does not necessarily take the entry off your screen. Saying which
// of these happened is the difference between "nothing happened" and "nothing you can fix".
internal enum ForgetOutcome
{
    // The record was removed and the destination is gone from this player's list.
    Removed,

    // The record was removed, but another player has also visited that trader and the access
    // mode shares their visits, so it is still listed. Nothing more this player can do - the
    // alternative would be deleting someone else's record.
    RemovedButStillListed,

    // This player never visited that trader; it is on their list only because someone else
    // did. Nothing was removed.
    NotVisitedByThisPlayer,
}
