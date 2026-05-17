using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

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

        if (!VisitedTraderNetwork.IsClientOnly)
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

        TraderDestination currentTrader = VisitedTraderStore.CreateCurrentTraderDestination(
            __instance.OwnerDialog?.CurrentOwner as EntityTrader);
        if (currentTrader == null)
        {
            currentTrader = DialogSessionStore.GetCurrentTrader(__instance.OwnerDialog);
        }

        EntityPlayer player = DialogSessionStore.GetPlayer(__instance.OwnerDialog);
        List<TraderDestination> destinations = VisitedTraderStore.GetDestinations(player)
            .Where(destination => !VisitedTraderStore.IsSameTrader(destination, currentTrader))
            .OrderBy(destination => TraderDestinationFormatter.GetDistanceSq(destination, player))
            .ThenBy(destination => destination.DisplayName, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(destination => destination.AreaX)
            .ThenBy(destination => destination.AreaZ)
            .ToList();
        var dynamicEntries = new List<BaseResponseEntry>();

        if (destinations.Count > 0)
        {
            foreach (TraderDestination destination in destinations)
            {
                dynamicEntries.Add(CreateDestinationEntry(__instance, destination, player));
            }
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
}

[HarmonyPatch(typeof(Dialog), nameof(Dialog.Cleanup))]
internal static class DialogCleanupPatch
{
    public static void Prefix(Dialog __instance)
    {
        DialogSessionStore.Remove(__instance);
    }
}
