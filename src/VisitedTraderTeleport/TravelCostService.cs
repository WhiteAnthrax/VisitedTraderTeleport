using System;
using UnityEngine;

namespace VisitedTraderTeleport;

internal static class TravelCostService
{
    private const int MaxCalculatedCost = 1000000;

    public static int CalculateCost(TraderDestination destination, EntityPlayer player, TravelCostSettings settings = null)
    {
        settings ??= GetEffectiveSettings();
        if (destination == null || player == null || settings == null || !settings.Enabled || settings.PerMeter <= 0f)
        {
            return 0;
        }

        Vector3 delta = destination.Position - player.position;
        delta.y = 0f;
        double rawDistanceCost = delta.magnitude * settings.PerMeter;
        int distanceCost = double.IsNaN(rawDistanceCost) ||
                           double.IsInfinity(rawDistanceCost) ||
                           rawDistanceCost >= MaxCalculatedCost
            ? MaxCalculatedCost
            : Mathf.CeilToInt((float)rawDistanceCost);
        return Math.Min(MaxCalculatedCost, Math.Max(settings.Minimum, distanceCost));
    }

    public static bool TryConsumeCost(EntityPlayer player, TraderDestination destination, out int cost)
    {
        TravelCostSettings settings = VisitedTraderTeleportConfig.TravelCost;
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

        int available = CountItems(player, itemValue, out int inventoryCount, out int bagCount);
        if (available < cost)
        {
            Debug.Log(
                $"[VisitedTraderTeleport] Travel cost blocked for {GetPlayerName(player)}: " +
                $"need {cost} {settings.ItemName}, available {available} " +
                $"(inventory={inventoryCount}, bag={bagCount}).");
            ShowInsufficientCost(player, cost, available, settings);
            return false;
        }

        RemoveItems(player, itemValue, cost);
        int remaining = CountItems(player, itemValue, out int remainingInventory, out int remainingBag);
        Debug.Log(
            $"[VisitedTraderTeleport] Consumed travel cost for {GetPlayerName(player)}: " +
            $"{cost} {settings.ItemName}; before={available} " +
            $"(inventory={inventoryCount}, bag={bagCount}), after={remaining} " +
            $"(inventory={remainingInventory}, bag={remainingBag}).");
        return true;
    }

    public static bool HasRequiredCost(EntityPlayer player, TraderDestination destination)
    {
        TravelCostSettings settings = VisitedTraderTeleportConfig.TravelCost;
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

        int available = CountItems(player, itemValue, out int inventoryCount, out int bagCount);
        if (available < cost)
        {
            Debug.Log(
                $"[VisitedTraderTeleport] Travel cost check failed for {GetPlayerName(player)}: " +
                $"need {cost} {settings.ItemName}, available {available} " +
                $"(inventory={inventoryCount}, bag={bagCount}).");
            ShowInsufficientCost(player, cost, available, settings);
            return false;
        }

        return true;
    }

    public static string FormatCostSuffix(TraderDestination destination, EntityPlayer player)
    {
        TravelCostSettings settings = GetEffectiveSettings();
        int cost = CalculateCost(destination, player, settings);
        if (cost <= 0)
        {
            return string.Empty;
        }

        string itemName = FormatItemDisplayName(settings);
        return VTTLocalization.Format("vtt_destination_cost_suffix", cost, itemName);
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

        GetDisplayRate(settings.PerMeter, out int amount, out int meters);
        string itemName = FormatItemDisplayName(settings);
        return settings.Minimum > 0
            ? VTTLocalization.Format("vtt_cost_info_minimum", amount, itemName, meters, settings.Minimum)
            : VTTLocalization.Format("vtt_cost_info", amount, itemName, meters);
    }

    public static string FormatItemDisplayName(TravelCostSettings settings)
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

        string localized = VTTLocalization.Get(settings.ItemName);
        if (!string.IsNullOrWhiteSpace(localized) && !string.Equals(localized, settings.ItemName, StringComparison.Ordinal))
        {
            return localized;
        }

        return string.IsNullOrWhiteSpace(settings.ItemDisplayName)
            ? settings.ItemName
            : settings.ItemDisplayName;
    }

    private static TravelCostSettings GetEffectiveSettings()
    {
        return VisitedTraderNetwork.IsClientOnly
            ? VisitedTraderClientState.ServerTravelCost
            : VisitedTraderTeleportConfig.TravelCost;
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

    private static int CountItems(EntityPlayer player, ItemValue itemValue, out int inventoryCount, out int bagCount)
    {
        inventoryCount = 0;
        bagCount = 0;
        if (player == null || itemValue == null)
        {
            return 0;
        }

        if (player.inventory != null)
        {
            inventoryCount = player.inventory.GetItemCount(itemValue);
        }

        if (player.bag != null)
        {
            bagCount = player.bag.GetItemCount(itemValue);
        }

        return inventoryCount + bagCount;
    }

    private static void RemoveItems(EntityPlayer player, ItemValue itemValue, int count)
    {
        if (player == null || itemValue == null || count <= 0)
        {
            return;
        }

        int remaining = count;
        if (player.inventory != null)
        {
            int inventoryCount = player.inventory.GetItemCount(itemValue);
            int fromInventory = Math.Min(inventoryCount, remaining);
            if (fromInventory > 0)
            {
                remaining -= player.inventory.DecItem(itemValue, fromInventory);
            }
        }

        if (remaining > 0 && player.bag != null)
        {
            player.bag.DecItem(itemValue, remaining, false);
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

    private static void GetDisplayRate(float perMeter, out int amount, out int meters)
    {
        if (perMeter >= 1f)
        {
            amount = Mathf.CeilToInt(perMeter);
            meters = 1;
            return;
        }

        amount = 1;
        meters = Math.Max(1, Mathf.RoundToInt(1f / perMeter));
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
