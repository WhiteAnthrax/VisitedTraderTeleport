using System;
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
    public const string PagePreviousResponseId = "vtt_destination_page_previous";
    public const string PageNextResponseId = "vtt_destination_page_next";
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
    internal const int DestinationsPerPage = 5;

    public static void Postfix(DialogStatement __instance, ref List<BaseResponseEntry> __result)
    {
        if (__instance?.ID != DialogIds.DestinationStatementId || __result == null)
        {
            return;
        }

        __instance.Text = DestinationStatementFormatter.Format(__instance.OwnerDialog);
        DialogDestinationState destinationState = DialogDestinationState.Create(__instance.OwnerDialog);
        List<TraderDestination> destinations = destinationState.VisibleDestinations;
        var dynamicEntries = new List<BaseResponseEntry>();

        if (destinations.Count > 0)
        {
            foreach (TraderDestination destination in destinations)
            {
                dynamicEntries.Add(CreateDestinationEntry(__instance, destination, destinationState.Player));
            }

            if (destinationState.TotalPages > 1)
            {
                if (destinationState.PageIndex > 0)
                {
                    dynamicEntries.Add(CreatePageEntry(
                        __instance,
                        DialogIds.PagePreviousResponseId,
                        VTTLocalization.Get("vtt_page_previous"),
                        -1));
                }

                if (destinationState.PageIndex < destinationState.TotalPages - 1)
                {
                    dynamicEntries.Add(CreatePageEntry(
                        __instance,
                        DialogIds.PageNextResponseId,
                        VTTLocalization.Get("vtt_page_next"),
                        1));
                }
            }
        }

        if (destinations.Count == 0)
        {
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

    private static BaseResponseEntry CreatePageEntry(DialogStatement statement, string id, string text, int delta)
    {
        var response = new DialogResponse(id)
        {
            Text = text,
            OwnerDialog = statement.OwnerDialog,
            Actions = new List<BaseDialogAction>(),
            NextStatementID = DialogIds.DestinationStatementId
        };

        var action = new DialogActionVisitedTraderPage
        {
            ID = "page",
            Value = delta.ToString(System.Globalization.CultureInfo.InvariantCulture),
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

[HarmonyPatch(typeof(Dialog), nameof(Dialog.GetStatement))]
internal static class DialogGetStatementPatch
{
    public static void Postfix(Dialog __instance, string currentStatementID, ref DialogStatement __result)
    {
        if (__result?.ID == DialogIds.DestinationStatementId)
        {
            __result.Text = DestinationStatementFormatter.Format(__instance);
        }
    }
}

[HarmonyPatch(typeof(XUiC_DialogStatementWindow), nameof(XUiC_DialogStatementWindow.GetBindingValueInternal))]
internal static class DialogStatementWindowGetBindingValuePatch
{
    public static bool Prefix(
        XUiC_DialogStatementWindow __instance,
        ref string value,
        string bindingName,
        ref bool __result)
    {
        if (!string.Equals(bindingName, "statement", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        Dialog dialog = __instance?.CurrentDialog;
        if (dialog?.CurrentStatement?.ID != DialogIds.DestinationStatementId)
        {
            return true;
        }

        value = DestinationStatementFormatter.Format(dialog);
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(XUiC_DialogRespondentName), nameof(XUiC_DialogRespondentName.GetBindingValueInternal))]
internal static class DialogRespondentNameGetBindingValuePatch
{
    public static bool Prefix(
        XUiC_DialogRespondentName __instance,
        ref string value,
        string bindingName,
        ref bool __result)
    {
        if (!string.Equals(bindingName, "respondentname", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        Dialog dialog = __instance?.CurrentDialog;
        if (dialog?.CurrentStatement?.ID != DialogIds.DestinationStatementId)
        {
            return true;
        }

        string respondentName = dialog.CurrentOwner?.EntityName;
        if (string.IsNullOrWhiteSpace(respondentName))
        {
            respondentName = value;
        }

        value = string.IsNullOrWhiteSpace(respondentName)
            ? DestinationStatementFormatter.FormatCompactStatus(dialog)
            : respondentName + " - " + DestinationStatementFormatter.FormatCompactStatus(dialog);
        __result = true;
        return false;
    }
}

internal static class DestinationStatementFormatter
{
    public static string Format(Dialog dialog)
    {
        DialogDestinationState destinationState = DialogDestinationState.Create(dialog);
        var lines = new List<string>
        {
            VTTLocalization.Get("vtt_statement_destinations"),
            TraderDialogStatusFormatter.FormatModeLine(destinationState.AccessMode)
        };

        if (destinationState.TotalDestinationCount > 0 && destinationState.TotalPages > 1)
        {
            lines.Add(VTTLocalization.Format(
                "vtt_page_info",
                destinationState.PageIndex + 1,
                destinationState.TotalPages,
                destinationState.TotalDestinationCount));
        }

        if (destinationState.TotalDestinationCount == 0)
        {
            lines.Add(VTTLocalization.Get("vtt_no_destinations"));
        }

        return string.Join("\n", lines);
    }

    public static string FormatCompactStatus(Dialog dialog)
    {
        DialogDestinationState destinationState = DialogDestinationState.Create(dialog);
        string modeName = TraderDialogStatusFormatter.FormatModeName(destinationState.AccessMode);
        if (destinationState.TotalDestinationCount > 0 && destinationState.TotalPages > 1)
        {
            return VTTLocalization.Format(
                "vtt_compact_status_paged",
                modeName,
                destinationState.PageIndex + 1,
                destinationState.TotalPages,
                destinationState.TotalDestinationCount);
        }

        return VTTLocalization.Format("vtt_compact_status", modeName);
    }
}

internal sealed class DialogDestinationState
{
    public EntityPlayer Player;
    public TraderDestination CurrentTrader;
    public AccessMode AccessMode;
    public int AllowedDestinationCount;
    public int TotalDestinationCount;
    public int PageIndex;
    public int TotalPages;
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

        int totalDestinationCount = visibleDestinations.Count;
        int totalPages = Math.Max(1, (totalDestinationCount + DialogStatementGetResponsesPatch.DestinationsPerPage - 1) / DialogStatementGetResponsesPatch.DestinationsPerPage);
        int pageIndex = Math.Min(Math.Max(0, DialogSessionStore.GetDestinationPage(dialog)), totalPages - 1);
        DialogSessionStore.SetDestinationPage(dialog, pageIndex);

        List<TraderDestination> pageDestinations = visibleDestinations
            .Skip(pageIndex * DialogStatementGetResponsesPatch.DestinationsPerPage)
            .Take(DialogStatementGetResponsesPatch.DestinationsPerPage)
            .ToList();

        return new DialogDestinationState
        {
            Player = player,
            CurrentTrader = currentTrader,
            AccessMode = GetCurrentAccessMode(),
            AllowedDestinationCount = allowedDestinations.Count,
            TotalDestinationCount = totalDestinationCount,
            PageIndex = pageIndex,
            TotalPages = totalPages,
            VisibleDestinations = pageDestinations
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
