using System;

namespace VisitedTraderTeleport;

internal static class TravelCostCalculator
{
    private const int MaxCalculatedCost = 1000000;

    public static int CalculateCost(float distanceMeters, TravelCostSettings settings)
    {
        if (settings == null || !settings.Enabled || settings.PerMeter <= 0f)
        {
            return 0;
        }

        double rawDistanceCost = distanceMeters * settings.PerMeter;
        int distanceCost = double.IsNaN(rawDistanceCost) ||
                           double.IsInfinity(rawDistanceCost) ||
                           rawDistanceCost >= MaxCalculatedCost
            ? MaxCalculatedCost
            : (int)Math.Ceiling(rawDistanceCost);
        return Math.Min(MaxCalculatedCost, Math.Max(settings.Minimum, distanceCost));
    }

    public static void GetDisplayRate(float perMeter, out int amount, out int meters)
    {
        if (perMeter >= 1f)
        {
            amount = (int)Math.Ceiling(perMeter);
            meters = 1;
            return;
        }

        amount = 1;
        meters = Math.Max(1, (int)Math.Round(1f / perMeter, MidpointRounding.AwayFromZero));
    }

    public static string FormatItemDisplayName(TravelCostSettings settings, ILocalizationProvider localization)
    {
        if (settings == null)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(settings.ItemName))
        {
            return string.IsNullOrWhiteSpace(settings.ItemDisplayName)
                ? string.Empty
                : settings.ItemDisplayName;
        }

        string localized = localization.Get(settings.ItemName);
        if (!string.IsNullOrWhiteSpace(localized) && !string.Equals(localized, settings.ItemName, StringComparison.Ordinal))
        {
            return localized;
        }

        return string.IsNullOrWhiteSpace(settings.ItemDisplayName)
            ? settings.ItemName
            : settings.ItemDisplayName;
    }

    public static int GetAvailableCount(IPlayerInventory inventory, string itemName)
    {
        return inventory.ItemExists(itemName) ? inventory.CountItem(itemName) : 0;
    }

    public static bool HasSufficientItems(IPlayerInventory inventory, string itemName, int cost, out int available)
    {
        available = GetAvailableCount(inventory, itemName);
        return cost <= 0 || available >= cost;
    }

    // availableBefore is passed in from the caller's prior HasSufficientItems call so this
    // does not re-query the same count before removing items.
    public static InventoryConsumptionResult ConsumeItems(IPlayerInventory inventory, string itemName, int cost, int availableBefore)
    {
        int removed = inventory.RemoveItem(itemName, cost);
        int remaining = inventory.CountItem(itemName);
        return new InventoryConsumptionResult(availableBefore, cost, removed, remaining);
    }
}
