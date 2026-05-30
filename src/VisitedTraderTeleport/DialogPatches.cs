using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VisitedTraderTeleport;

internal static class DialogIds
{
    public const string TraderDialogId = "trader";
    public const string StartStatementId = "start";
    public const string DestinationStatementId = "vtt_destinations";
    public const string ConfirmStatementId = "vtt_confirm";
    public const string StartStatusResponseId = "vtt_status_start";
    public const string DestinationStatusResponseId = "vtt_status_destinations";
    public const string DynamicResponsePrefix = "vtt_destination_";
    public const string PagePreviousResponseId = "vtt_destination_page_previous";
    public const string PageNextResponseId = "vtt_destination_page_next";
    public const string ConfirmYesResponseId = "vtt_confirm_yes";
    public const string ConfirmInfoResponseId = "vtt_confirm_infoline";
    public const string ConfirmPromptResponseId = "vtt_confirm_promptline";
    public const string ConfirmCostResponseId = "vtt_confirm_costline";
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
        if (__instance == null || __result == null)
        {
            return;
        }

        if (__instance.OwnerDialog?.ID == DialogIds.TraderDialogId && __instance.ID == DialogIds.StartStatementId)
        {
            UpdateOpenResponseText(__result);
            return;
        }

        if (__instance.ID == DialogIds.ConfirmStatementId)
        {
            BuildConfirmResponses(__instance, __result);
            return;
        }

        if (__instance.ID != DialogIds.DestinationStatementId)
        {
            return;
        }

        __instance.Text = DestinationStatementFormatter.Format(__instance.OwnerDialog);
        DialogDestinationState destinationState = DialogDestinationState.Create(__instance.OwnerDialog);
        List<TraderDestination> destinations = destinationState.VisibleDestinations;
        var dynamicEntries = new List<BaseResponseEntry>
        {
            CreateStatusEntry(
                __instance,
                DialogIds.DestinationStatusResponseId,
                DestinationStatementFormatter.FormatDestinationStatus(destinationState))
        };

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

        if (ConfirmationService.RequiresConfirmation(destination, player))
        {
            response.NextStatementID = DialogIds.ConfirmStatementId;
            response.Actions.Add(new DialogActionVisitedTraderConfirm
            {
                ID = "confirm",
                Value = destination.Key,
                OwnerDialog = statement.OwnerDialog,
                Owner = response
            });
        }
        else
        {
            response.Actions.Add(new DialogActionVisitedTraderTeleport
            {
                ID = "teleport",
                Value = destination.Key,
                OwnerDialog = statement.OwnerDialog,
                Owner = response
            });
        }

        return new DialogResponseEntry(response.ID)
        {
            Response = response
        };
    }

    private static void BuildConfirmResponses(DialogStatement statement, List<BaseResponseEntry> responses)
    {
        Dialog dialog = statement.OwnerDialog;
        EntityPlayer player = DialogSessionStore.GetPlayer(dialog);
        string pendingKey = DialogSessionStore.GetPendingDestination(dialog);
        ConfirmationService.TryResolveDestination(pendingKey, player, out TraderDestination destination);

        string question = ConfirmationService.FormatPromptQuestion(destination);
        string costLine = ConfirmationService.FormatCostLine(destination, player);
        statement.Text = question;

        // The trader dialog skin renders the response list but not the statement body, so
        // show the prompt as a non-selectable response entry. The cost goes on its own
        // entry because a combined line is too wide and gets clipped.
        var entries = new List<BaseResponseEntry>();
        if (destination != null)
        {
            // Carry over the same detail line the destination list showed
            // (distance, direction, coordinates, biome).
            entries.Add(CreateStatusEntry(
                statement,
                DialogIds.ConfirmInfoResponseId,
                TraderDestinationFormatter.FormatResponse(destination, player)));
        }

        entries.Add(CreateStatusEntry(statement, DialogIds.ConfirmPromptResponseId, question));
        if (!string.IsNullOrWhiteSpace(costLine))
        {
            entries.Add(CreateStatusEntry(statement, DialogIds.ConfirmCostResponseId, costLine));
        }

        var yes = new DialogResponse(DialogIds.ConfirmYesResponseId)
        {
            Text = VTTLocalization.Get("vtt_confirm_yes"),
            OwnerDialog = dialog,
            Actions = new List<BaseDialogAction>()
        };
        yes.Actions.Add(new DialogActionVisitedTraderTeleport
        {
            ID = "teleport",
            Value = pendingKey,
            OwnerDialog = dialog,
            Owner = yes
        });
        entries.Add(new DialogResponseEntry(yes.ID) { Response = yes });

        // "No" is a static response (vtt_confirm_no) defined in dialogs.xml that returns to
        // the destination list, so the confirmation screen no longer also shows the vanilla
        // "nevermind" exit. Insert the prompt, cost, and Yes ahead of it.
        responses.InsertRange(0, entries);
    }

    private static void InsertStatusEntry(DialogStatement statement, List<BaseResponseEntry> responses, string id, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        responses.Insert(0, CreateStatusEntry(statement, id, text));
    }

    private static BaseResponseEntry CreateStatusEntry(DialogStatement statement, string id, string text)
    {
        var response = new DialogResponse(id)
        {
            Text = text,
            OwnerDialog = statement.OwnerDialog,
            Actions = new List<BaseDialogAction>(),
            NextStatementID = statement.ID
        };

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

    private static void UpdateOpenResponseText(List<BaseResponseEntry> responses)
    {
        string text = VTTLocalization.Get("vtt_response_open") + TravelCostService.FormatOpenResponseCostSuffix();
        foreach (BaseResponseEntry entry in responses)
        {
            if (entry?.Response?.ID == "vtt_open")
            {
                entry.Response.Text = text;
            }
        }
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
        else if (__result?.ID == DialogIds.ConfirmStatementId)
        {
            __result.Text = FormatConfirmStatement(__instance);
        }
    }

    internal static string FormatConfirmStatement(Dialog dialog)
    {
        EntityPlayer player = DialogSessionStore.GetPlayer(dialog);
        string pendingKey = DialogSessionStore.GetPendingDestination(dialog);
        ConfirmationService.TryResolveDestination(pendingKey, player, out TraderDestination destination);
        return ConfirmationService.FormatPromptQuestion(destination);
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
        string statementId = dialog?.CurrentStatement?.ID;
        if (statementId == DialogIds.DestinationStatementId)
        {
            value = DestinationStatementFormatter.Format(dialog);
            __result = true;
            return false;
        }

        if (statementId == DialogIds.ConfirmStatementId)
        {
            value = DialogGetStatementPatch.FormatConfirmStatement(dialog);
            __result = true;
            return false;
        }

        return true;
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
        return FormatCompactStatus(destinationState);
    }

    public static string FormatCompactStatus(DialogDestinationState destinationState)
    {
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

    public static string FormatDestinationStatus(DialogDestinationState destinationState)
    {
        return FormatStatusResponse(FormatCompactStatus(destinationState));
    }

    private static string FormatStatusResponse(string status)
    {
        string cost = TravelCostService.FormatStatusCostInfo();
        return string.IsNullOrWhiteSpace(cost)
            ? VTTLocalization.Format("vtt_status_response", status)
            : VTTLocalization.Format("vtt_status_response_with_cost", status, cost);
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

    public static AccessMode GetCurrentAccessMode()
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
