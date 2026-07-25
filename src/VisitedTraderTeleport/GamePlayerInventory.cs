using System;
using System.Collections.Generic;

namespace VisitedTraderTeleport;

internal sealed class GamePlayerInventory : IPlayerInventory
{
    private readonly EntityPlayer player;

    public GamePlayerInventory(EntityPlayer player)
    {
        this.player = player;
    }

    public bool ItemExists(string itemName)
    {
        return !string.IsNullOrWhiteSpace(itemName) && ItemClass.GetItemClass(itemName, false) != null;
    }

    public int CountItem(string itemName)
    {
        if (!TryGetItemValue(itemName, out ItemValue itemValue))
        {
            return 0;
        }

        return CountItemValue(itemValue, out _, out _);
    }

    public int RemoveItem(string itemName, int count)
    {
        if (!TryGetItemValue(itemName, out ItemValue itemValue) || count <= 0)
        {
            return 0;
        }

        int remaining = count;
        int removed = 0;
        IList<ItemStack> removedItems = new List<ItemStack>();
        if (player?.inventory != null)
        {
            int inventoryCount = player.inventory.GetItemCount(itemValue);
            int fromInventory = Math.Min(inventoryCount, remaining);
            if (fromInventory > 0)
            {
                int removedFromInventory = player.inventory.DecItem(itemValue, fromInventory, true, removedItems);
                removed += removedFromInventory;
                remaining -= removedFromInventory;
            }
        }

        if (remaining > 0 && player?.bag != null)
        {
            removed += player.bag.DecItem(itemValue, remaining, true, removedItems);
        }

        return removed;
    }

    // Diagnostic-only breakdown used by the caller's logging; not part of IPlayerInventory
    // because TravelCostCalculator only ever needs the combined count. This intentionally
    // re-queries the same counts CountItem/RemoveItem already look at, kept separate so the
    // seam interface itself stays free of logging concerns.
    public (int inventoryCount, int bagCount) GetBreakdown(string itemName)
    {
        if (!TryGetItemValue(itemName, out ItemValue itemValue))
        {
            return (0, 0);
        }

        CountItemValue(itemValue, out int inventoryCount, out int bagCount);
        return (inventoryCount, bagCount);
    }

    private int CountItemValue(ItemValue itemValue, out int inventoryCount, out int bagCount)
    {
        inventoryCount = player?.inventory?.GetItemCount(itemValue) ?? 0;
        bagCount = player?.bag?.GetItemCount(itemValue) ?? 0;
        return inventoryCount + bagCount;
    }

    private static bool TryGetItemValue(string itemName, out ItemValue itemValue)
    {
        itemValue = null;
        if (string.IsNullOrWhiteSpace(itemName) || ItemClass.GetItemClass(itemName, false) == null)
        {
            return false;
        }

        itemValue = ItemClass.GetItem(itemName, false);
        return itemValue != null;
    }
}
