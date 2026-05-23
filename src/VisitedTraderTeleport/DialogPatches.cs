using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VisitedTraderTeleport;

internal static class DialogIds
{
    public const string TraderDialogId = "trader";
    public const string DestinationStatementId = "vtt_destinations";
    public const string DynamicResponsePrefix = "vtt_destination_";
}

[HarmonyPatch(typeof(Dialog), nameof(Dialog.GetFirstStatment))]
internal static class DialogGetFirstStatementPatch
{
    public static void Postfix(Dialog __instance, EntityPlayer player)
    {
        if (__instance?.ID != DialogIds.TraderDialogId)
        {
            return;
        }

        if (__instance.CurrentOwner is EntityTrader trader)
        {
            DialogSessionStore.Set(__instance, player, VisitedTraderStore.CreateCurrentTraderDestination(trader));
            VisitedTraderStore.Record(trader, player);
        }
        else
        {
            DialogSessionStore.Set(__instance, player, null);
        }

        if (VisitedTraderNetwork.IsClientOnly)
        {
            VisitedTraderNetwork.RequestSnapshot();
        }
    }
}

[HarmonyPatch(typeof(DialogStatement), nameof(DialogStatement.GetResponses))]
internal static class DialogStatementGetResponsesPatch
{
    public static void Postfix(DialogStatement __instance, ref List<BaseResponseEntry> __result)
    {
        if (__instance?.ID != DialogIds.DestinationStatementId || __result == null)
        {
            return;
        }

        DialogDestinationState destinationState = DialogDestinationState.Create(__instance.OwnerDialog);
        List<TraderDestination> destinations = destinationState.VisibleDestinations;
        var dynamicEntries = new List<BaseResponseEntry>();
        dynamicEntries.Add(CreateInfoEntry(
            __instance,
            "vtt_mode_info",
            TraderDialogStatusFormatter.FormatModeLine(destinationState.AccessMode)));

        if (destinations.Count > 0)
        {
            foreach (TraderDestination destination in destinations)
            {
                dynamicEntries.Add(CreateDestinationEntry(__instance, destination, destinationState.Player));
            }
        }

        if (destinations.Count == 0)
        {
            dynamicEntries.Add(CreateInfoEntry(
                __instance,
                "vtt_no_destinations_info",
                VTTLocalization.Get("vtt_no_destinations")));
            Debug.Log(
                $"[VisitedTraderTeleport] No destinations shown for {DialogDestinationState.GetPlayerName(destinationState.Player)}: " +
                $"mode={destinationState.AccessMode}, allowed={destinationState.AllowedDestinationCount}, " +
                $"visible=0, current={destinationState.CurrentTraderText}.");
        }

        __result.InsertRange(0, dynamicEntries);
    }

    private static BaseResponseEntry CreateDestinationEntry(DialogStatement statement, TraderDestination destination, EntityPlayer player)
    {
        string responseId = DialogIds.DynamicResponsePrefix + unchecked((uint)destination.Key.GetHashCode()).ToString("X8");
        var response = new DialogResponse(responseId)
        {
            Text = TraderDestinationFormatter.FormatResponse(destination, player),
            OwnerDialog = statement.OwnerDialog,
            Actions = new List<BaseDialogAction>()
        };

        var action = new DialogActionVisitedTraderTeleport
        {
            ID = "teleport",
            Value = destination.Key,
            OwnerDialog = statement.OwnerDialog,
            Owner = response
        };
        response.Actions.Add(action);

        return new DialogResponseEntry(response.ID)
        {
            Response = response
        };
    }

    private static BaseResponseEntry CreateInfoEntry(DialogStatement statement, string id, string text)
    {
        var response = new DialogResponse(id)
        {
            Text = text,
            OwnerDialog = statement.OwnerDialog,
            Actions = new List<BaseDialogAction>(),
            NextStatementID = DialogIds.DestinationStatementId
        };

        return new DialogResponseEntry(response.ID)
        {
            Response = response
        };
    }
}

internal sealed class DialogDestinationState
{
    public EntityPlayer Player;
    public TraderDestination CurrentTrader;
    public AccessMode AccessMode;
    public int AllowedDestinationCount;
    public List<TraderDestination> VisibleDestinations = new();

    public string CurrentTraderText => CurrentTrader?.DialogText ?? "unknown trader";

    public static DialogDestinationState Create(Dialog dialog, EntityPlayer player = null)
    {
        player ??= DialogSessionStore.GetPlayer(dialog);

        TraderDestination currentTrader = VisitedTraderStore.CreateCurrentTraderDestination(
            dialog?.CurrentOwner as EntityTrader);
        if (currentTrader == null)
        {
            currentTrader = DialogSessionStore.GetCurrentTrader(dialog);
        }

        IReadOnlyList<TraderDestination> allowedDestinations = VisitedTraderStore.GetDestinations(player);
        List<TraderDestination> visibleDestinations = allowedDestinations
            .Where(destination => !VisitedTraderStore.IsSameTrader(destination, currentTrader))
            .OrderBy(destination => TraderDestinationFormatter.GetDistanceSq(destination, player))
            .ThenBy(destination => destination.DisplayName, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(destination => destination.AreaX)
            .ThenBy(destination => destination.AreaZ)
            .ToList();

        return new DialogDestinationState
        {
            Player = player,
            CurrentTrader = currentTrader,
            AccessMode = GetCurrentAccessMode(),
            AllowedDestinationCount = allowedDestinations.Count,
            VisibleDestinations = visibleDestinations
        };
    }

    public static string GetPlayerName(EntityPlayer player)
    {
        return string.IsNullOrWhiteSpace(player?.PlayerDisplayName)
            ? "unknown player"
            : player.PlayerDisplayName;
    }

    private static AccessMode GetCurrentAccessMode()
    {
        return VisitedTraderNetwork.IsClientOnly
            ? VisitedTraderClientState.ServerAccessMode
            : VisitedTraderTeleportConfig.AccessMode;
    }
}

[HarmonyPatch(typeof(Dialog), nameof(Dialog.Cleanup))]
internal static class DialogCleanupPatch
{
    public static void Prefix(Dialog __instance)
    {
        DialogSessionStore.Remove(__instance);
    }
}
