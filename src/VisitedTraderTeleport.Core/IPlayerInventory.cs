namespace VisitedTraderTeleport;

// ItemExists checks the game's item catalog (does this item id exist at all), not the
// player's personal inventory - it lives here because callers always need both checks
// together, not because it's conceptually per-player.
internal interface IPlayerInventory
{
    bool ItemExists(string itemName);

    int CountItem(string itemName);

    int RemoveItem(string itemName, int count);
}
