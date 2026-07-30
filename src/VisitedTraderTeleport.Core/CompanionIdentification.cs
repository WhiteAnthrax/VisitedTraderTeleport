namespace VisitedTraderTeleport;

// Decides whether an entity is a companion the player's travel should pull along.
//
// This used to be a blocklist - "owned by the player, and not one of the types we know are
// not companions" - and it missed a real player-owned EntityAlive subtype twice: drivable
// vehicles, then placed turrets. Both are owned, neither follows anybody, and a turret
// being uprooted from its base and dropped at a trader is a good deal worse than a
// companion being left behind.
//
// So the test is positive now: a companion is an entity SCore has marked as hired.
// SCore's own EntityUtilities.IsHired is exactly `GetLeaderOrOwner(id) != null`, and
// GetLeaderOrOwner reads the "Leader" and "Owner" Buffs custom vars - so checking those two
// vars is the same question SCore asks, without taking a dependency on SCore being loaded.
//
// What is deliberately *not* consulted any more is EntityAlive.belongsPlayerId. That field
// means "this player owns it", which is true of turrets, vehicles and drones alike; it was
// never a statement about following anyone. Reading ownership as companionship is what
// caused both misses.
internal static class CompanionIdentification
{
    // Kept as a second line of defence rather than as the decision itself. If some framework
    // ever does set an Owner/Leader var on a vehicle or drone, travel still must not drag it
    // along, and the caller can say so without this file knowing the game's type hierarchy.
    public static bool IsCompanion(
        bool isExcludedType,
        bool hasLeaderVar,
        int leaderValue,
        bool hasOwnerVar,
        int ownerValue,
        int playerId)
    {
        if (isExcludedType)
        {
            return false;
        }

        // Matches SCore's GetLeaderOrOwner order: Leader first, Owner as the fallback.
        if (hasLeaderVar && leaderValue == playerId)
        {
            return true;
        }

        return hasOwnerVar && ownerValue == playerId;
    }
}
