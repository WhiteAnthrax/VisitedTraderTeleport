using UnityEngine;

namespace VisitedTraderTeleport;

internal static class TravelCostService
{
    public static int CalculateCost(TraderDestination destination, EntityPlayer player, TravelCostSettings settings = null)
    {
        settings ??= GetEffectiveSettings();
        if (destination == null || player == null)
        {
            return 0;
        }

        return TravelCostCalculator.CalculateCost(CalculateDistanceMeters(destination, player), settings);
    }

    public static bool TryConsumeCost(EntityPlayer player, TraderDestination destination, out int cost)
    {
        TravelCostSettings settings = GetEffectiveSettings();
        cost = CalculateCost(destination, player, settings);
        if (cost <= 0)
        {
            return true;
        }

        if (!TryGetItemValue(settings.ItemName, out ItemValue itemValue))
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Travel cost item not found: {settings.ItemName}. Travel blocked.");
            ShowCostUnavailable(player);
            return false;
        }

        var inventory = new GamePlayerInventory(player);
        if (!TravelCostCalculator.HasSufficientItems(inventory, settings.ItemName, cost, out int available))
        {
            Debug.Log(
                $"[VisitedTraderTeleport] Travel cost blocked for {GetPlayerName(player)}: " +
                $"need {cost} {settings.ItemName}, available {available}.");
            ShowInsufficientCost(player, cost, available, settings);
            return false;
        }

        (int inventoryCount, int bagCount) = inventory.GetBreakdown(settings.ItemName);
        InventoryConsumptionResult result = TravelCostCalculator.ConsumeItems(inventory, settings.ItemName, cost, available);
        (int remainingInventory, int remainingBag) = inventory.GetBreakdown(settings.ItemName);

        if (result.UnderConsumed)
        {
            Debug.LogWarning(
                $"[VisitedTraderTeleport] Travel cost removal failed for {GetPlayerName(player)}: " +
                $"need to remove {cost} {settings.ItemName}, removed={result.Removed}, " +
                $"before={available} (inventory={inventoryCount}, bag={bagCount}), " +
                $"after={result.RemainingAfter} (inventory={remainingInventory}, bag={remainingBag}). Travel blocked.");
            ShowCostUnavailable(player);
            return false;
        }

        if (result.OverConsumed)
        {
            Debug.LogWarning(
                $"[VisitedTraderTeleport] Travel cost removal removed more than expected for {GetPlayerName(player)}: " +
                $"cost={cost} {settings.ItemName}, removed={result.Removed}, " +
                $"before={available} (inventory={inventoryCount}, bag={bagCount}), " +
                $"after={result.RemainingAfter} (inventory={remainingInventory}, bag={remainingBag}).");
        }

        ShowLocalCostRemoval(player, itemValue, cost);
        Debug.Log(
            $"[VisitedTraderTeleport] Consumed travel cost for {GetPlayerName(player)}: " +
            $"{cost} {settings.ItemName}; removed={result.Removed}, before={available} " +
            $"(inventory={inventoryCount}, bag={bagCount}), after={result.RemainingAfter} " +
            $"(inventory={remainingInventory}, bag={remainingBag}).");
        return true;
    }

    public static void TryConsumeLocalCost(EntityPlayerLocal player, string itemName, int cost)
    {
        if (player == null || cost <= 0 || string.IsNullOrWhiteSpace(itemName))
        {
            return;
        }

        if (!TryGetItemValue(itemName, out ItemValue itemValue))
        {
            Debug.LogWarning(
                $"[VisitedTraderTeleport] Travel cost item not found on client: {itemName}. Skipping local consumption.");
            return;
        }

        var inventory = new GamePlayerInventory(player);
        int available = TravelCostCalculator.GetAvailableCount(inventory, itemName);
        (int inventoryCount, int bagCount) = inventory.GetBreakdown(itemName);
        InventoryConsumptionResult result = TravelCostCalculator.ConsumeItems(inventory, itemName, cost, available);
        (int remainingInventory, int remainingBag) = inventory.GetBreakdown(itemName);

        if (result.UnderConsumed)
        {
            Debug.LogWarning(
                $"[VisitedTraderTeleport] Local travel cost removal under-consumed for {GetPlayerName(player)}: " +
                $"cost={cost} {itemName}, removed={result.Removed}, " +
                $"before={available} (inventory={inventoryCount}, bag={bagCount}), " +
                $"after={result.RemainingAfter} (inventory={remainingInventory}, bag={remainingBag}).");
        }
        else if (result.OverConsumed)
        {
            Debug.LogWarning(
                $"[VisitedTraderTeleport] Local travel cost removal over-consumed for {GetPlayerName(player)}: " +
                $"cost={cost} {itemName}, removed={result.Removed}, " +
                $"before={available} (inventory={inventoryCount}, bag={bagCount}), " +
                $"after={result.RemainingAfter} (inventory={remainingInventory}, bag={remainingBag}).");
        }

        if (result.Removed > 0)
        {
            ShowLocalCostRemoval(player, itemValue, result.Removed);
        }

        Debug.Log(
            $"[VisitedTraderTeleport] Consumed local travel cost for {GetPlayerName(player)}: " +
            $"{cost} {itemName}; removed={result.Removed}, before={available} " +
            $"(inventory={inventoryCount}, bag={bagCount}), after={result.RemainingAfter} " +
            $"(inventory={remainingInventory}, bag={remainingBag}).");
    }

    public static bool HasRequiredCost(EntityPlayer player, TraderDestination destination)
    {
        TravelCostSettings settings = GetEffectiveSettings();
        int cost = CalculateCost(destination, player, settings);
        if (cost <= 0)
        {
            return true;
        }

        if (!TryGetItemValue(settings.ItemName, out ItemValue itemValue))
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Travel cost item not found: {settings.ItemName}. Travel blocked.");
            ShowCostUnavailable(player);
            return false;
        }

        var inventory = new GamePlayerInventory(player);
        if (!TravelCostCalculator.HasSufficientItems(inventory, settings.ItemName, cost, out int available))
        {
            (int inventoryCount, int bagCount) = inventory.GetBreakdown(settings.ItemName);
            Debug.Log(
                $"[VisitedTraderTeleport] Travel cost check failed for {GetPlayerName(player)}: " +
                $"need {cost} {settings.ItemName}, available {available} " +
                $"(inventory={inventoryCount}, bag={bagCount}).");
            ShowInsufficientCost(player, cost, available, settings);
            return false;
        }

        return true;
    }

    public static bool TryGetCostInfo(TraderDestination destination, EntityPlayer player, out int cost, out string itemDisplayName)
    {
        TravelCostSettings settings = GetEffectiveSettings();
        cost = CalculateCost(destination, player, settings);
        itemDisplayName = cost > 0 ? FormatItemDisplayName(settings) : string.Empty;
        return cost > 0;
    }

    public static string FormatOpenResponseCostSuffix()
    {
        return string.Empty;
    }

    public static string FormatStatusCostInfo()
    {
        TravelCostSettings settings = GetEffectiveSettings();
        if (settings == null || !settings.Enabled || settings.PerMeter <= 0f)
        {
            return string.Empty;
        }

        TravelCostCalculator.GetDisplayRate(settings.PerMeter, out int amount, out int meters);
        string itemName = FormatItemDisplayName(settings);
        return settings.Minimum > 0
            ? VTTLocalization.Format("vtt_cost_info_minimum", amount, itemName, meters, settings.Minimum)
            : VTTLocalization.Format("vtt_cost_info", amount, itemName, meters);
    }

    public static string FormatItemDisplayName(TravelCostSettings settings)
    {
        return TravelCostCalculator.FormatItemDisplayName(settings, GameLocalizationProvider.Instance);
    }

    private static TravelCostSettings GetEffectiveSettings()
    {
        return VisitedTraderNetwork.IsClientOnly
            ? VisitedTraderClientState.ServerTravelCost
            : VisitedTraderTeleportConfig.TravelCost;
    }

    private static float CalculateDistanceMeters(TraderDestination destination, EntityPlayer player)
    {
        Vector3 delta = destination.Position.ToVector3() - player.position;
        delta.y = 0f;
        return delta.magnitude;
    }

    private static bool TryGetItemValue(string itemName, out ItemValue itemValue)
    {
        itemValue = null;
        if (string.IsNullOrWhiteSpace(itemName) ||
            ItemClass.GetItemClass(itemName, false) == null)
        {
            return false;
        }

        itemValue = ItemClass.GetItem(itemName, false);
        return itemValue != null;
    }

    private static void ShowLocalCostRemoval(EntityPlayer player, ItemValue itemValue, int count)
    {
        if (player is EntityPlayerLocal localPlayer && count > 0)
        {
            localPlayer.AddUIHarvestingItem(new ItemStack(itemValue, -count));
        }
    }

    private static void ShowCostUnavailable(EntityPlayer player)
    {
        ShowTooltip(player, VTTLocalization.Get("vtt_travel_cost_unavailable"));
    }

    private static void ShowInsufficientCost(EntityPlayer player, int cost, int available, TravelCostSettings settings)
    {
        string itemName = FormatItemDisplayName(settings);
        ShowTooltip(player, VTTLocalization.Format("vtt_not_enough_travel_cost", cost, itemName, available));
    }

    private static void ShowTooltip(EntityPlayer player, string message)
    {
        if (player is EntityPlayerLocal localPlayer)
        {
            GameManager.ShowTooltip(localPlayer, message, false, false, 4f);
        }
        else
        {
            GameManager.ShowTooltipMP(player, string.Empty, message);
        }
    }

    private static string GetPlayerName(EntityPlayer player)
    {
        return string.IsNullOrWhiteSpace(player?.PlayerDisplayName)
            ? "unknown player"
            : player.PlayerDisplayName;
    }
}
