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
    public const string ForgetStatementId = "vtt_forget_confirm";
    public const string ForgetResponseId = "vtt_forget";
    public const string ForgetYesResponseId = "vtt_forget_yes";
    public const string ForgetInfoResponseId = "vtt_forget_infoline";
    public const string ForgetPromptResponseId = "vtt_forget_promptline";
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

        if (__instance.ID == DialogIds.ForgetStatementId)
        {
            BuildForgetResponses(__instance, __result);
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

        // Always a screen, never a jump straight to the trip. That screen is where forgetting
        // a destination lives, and there is nowhere else to put it: the destination list is
        // paged, so pairing every entry with a second one would halve the page and read badly.
        //
        // It costs a click for players who have confirmation switched off. For everyone else
        // it is the same screen they already saw, with one more thing on it - the prompt and
        // the cost line still appear only when confirmation is called for.
        response.NextStatementID = DialogIds.ConfirmStatementId;
        response.Actions.Add(new DialogActionVisitedTraderConfirm
        {
            ID = "confirm",
            Value = destination.Key,
            OwnerDialog = statement.OwnerDialog,
            Owner = response
        });

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

        // The prompt and the cost line are the *confirmation*, and they still follow the
        // Confirmation setting. What is always here is the list of things you can do with the
        // destination you picked, which is a different question from "are you sure".
        bool confirming = ConfirmationService.RequiresConfirmation(destination, player);
        string question = ConfirmationService.FormatPromptQuestion(destination);
        string costLine = confirming ? ConfirmationService.FormatCostLine(destination, player) : string.Empty;
        statement.Text = question;

        // The trader dialog skin renders the response list but not the statement body, so
        // show the prompt as a response entry. These lines are informational, so dim them
        // (like the status header) to set them apart from the selectable Yes/No. The cost
        // goes on its own entry because a combined line is too wide and gets clipped.
        var entries = new List<BaseResponseEntry>();
        if (destination != null)
        {
            // Carry over the same detail line the destination list showed
            // (distance, direction, coordinates, biome).
            entries.Add(CreateStatusEntry(
                statement,
                DialogIds.ConfirmInfoResponseId,
                DimInfo(TraderDestinationFormatter.FormatResponse(destination, player))));
        }

        if (confirming)
        {
            entries.Add(CreateStatusEntry(statement, DialogIds.ConfirmPromptResponseId, DimInfo(question)));
        }

        if (!string.IsNullOrWhiteSpace(costLine))
        {
            entries.Add(CreateStatusEntry(statement, DialogIds.ConfirmCostResponseId, DimInfo(costLine)));
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

        // Forgetting asks again before it does anything. It is recoverable - visit the trader
        // and it comes back - but a destination lost to a misclick is still worse than a
        // wasted click, and this sits directly under the button people came here to press.
        var forget = new DialogResponse(DialogIds.ForgetResponseId)
        {
            Text = VTTLocalization.Get("vtt_forget"),
            OwnerDialog = dialog,
            NextStatementID = DialogIds.ForgetStatementId,
            Actions = new List<BaseDialogAction>()
        };
        entries.Add(new DialogResponseEntry(forget.ID) { Response = forget });

        // "No" is a static response (vtt_confirm_no) defined in dialogs.xml that returns to
        // the destination list, so this screen no longer also shows the vanilla "nevermind"
        // exit. Insert the prompt, cost, and the actions ahead of it.
        responses.InsertRange(0, entries);
    }

    // "Do you really want to forget X?" - the last screen before a record is deleted.
    private static void BuildForgetResponses(DialogStatement statement, List<BaseResponseEntry> responses)
    {
        Dialog dialog = statement.OwnerDialog;
        EntityPlayer player = DialogSessionStore.GetPlayer(dialog);
        string pendingKey = DialogSessionStore.GetPendingDestination(dialog);
        ConfirmationService.TryResolveDestination(pendingKey, player, out TraderDestination destination);

        string question = destination == null
            ? VTTLocalization.Get("vtt_statement_forget")
            : VTTLocalization.Format("vtt_forget_prompt", TraderDestinationFormatter.FormatName(destination));
        statement.Text = question;

        var entries = new List<BaseResponseEntry>();
        if (destination != null)
        {
            entries.Add(CreateStatusEntry(
                statement,
                DialogIds.ForgetInfoResponseId,
                DimInfo(TraderDestinationFormatter.FormatResponse(destination, player))));
        }

        entries.Add(CreateStatusEntry(statement, DialogIds.ForgetPromptResponseId, DimInfo(question)));

        var yes = new DialogResponse(DialogIds.ForgetYesResponseId)
        {
            Text = VTTLocalization.Get("vtt_forget_yes"),
            OwnerDialog = dialog,
            // Back to the list, which is where the player can see the result. On a client the
            // list is a snapshot and the server's reply lands a moment later, so a slow
            // connection can show the old list once more; the tooltip says what happened
            // either way, and the next time the list is opened it is right.
            NextStatementID = DialogIds.DestinationStatementId,
            Actions = new List<BaseDialogAction>()
        };
        yes.Actions.Add(new DialogActionVisitedTraderForget
        {
            ID = "forget",
            Value = pendingKey,
            OwnerDialog = dialog,
            Owner = yes
        });
        entries.Add(new DialogResponseEntry(yes.ID) { Response = yes });

        responses.InsertRange(0, entries);
    }

    private const string InfoColor = "B0B0B0";

    // Dim informational lines so they read as context, not as selectable options.
    private static string DimInfo(string text)
    {
        return string.IsNullOrEmpty(text) ? text : $"[{InfoColor}]{text}[-]";
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
            TraderDialogStatusFormatter.FormatModeLine(destinationState.AccessMode, GameLocalizationProvider.Instance)
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
        string modeName = TraderDialogStatusFormatter.FormatModeName(destinationState.AccessMode, GameLocalizationProvider.Instance);
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
