using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace TraderTeleport;

internal static class DialogIds
{
    public const string TraderDialogId = "trader";
    public const string DestinationStatementId = "tt_destinations";
    public const string DynamicResponsePrefix = "tt_destination_";
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
            VisitedTraderStore.Record(trader, player);
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

        string currentTraderKey = VisitedTraderStore.GetKey(__instance.OwnerDialog?.CurrentOwner as EntityTrader);
        List<TraderDestination> destinations = VisitedTraderStore.GetDestinations()
            .Where(destination => destination.Key != currentTraderKey)
            .ToList();
        var dynamicEntries = new List<BaseResponseEntry>();

        if (destinations.Count > 0)
        {
            foreach (TraderDestination destination in destinations)
            {
                dynamicEntries.Add(CreateDestinationEntry(__instance, destination));
            }
        }

        __result.InsertRange(0, dynamicEntries);
    }

    private static BaseResponseEntry CreateDestinationEntry(DialogStatement statement, TraderDestination destination)
    {
        string responseId = DialogIds.DynamicResponsePrefix + unchecked((uint)destination.Key.GetHashCode()).ToString("X8");
        var response = new DialogResponse(responseId)
        {
            Text = destination.DialogText,
            OwnerDialog = statement.OwnerDialog,
            Actions = new List<BaseDialogAction>()
        };

        var action = new DialogActionTraderTeleport
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
