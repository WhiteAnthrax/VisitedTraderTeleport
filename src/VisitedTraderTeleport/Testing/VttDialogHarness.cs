#if VTT_TEST_HARNESS
using System.Collections.Generic;
using System.Text;

namespace VisitedTraderTeleport;

// Drives the real trader dialog UI rather than a data-level stand-in: it opens the game's own
// "dialog" window group against a real trader and activates responses the same way
// XUiC_DialogResponseList.OnPressResponse does, so the Harmony patches in DialogPatches.cs, the
// localization lookups, and the XUi rendering all run exactly as they do for a player.
//
// Confirmed against Assembly-CSharp.dll (2026-07-29, 7DTD 3.1.0 b13) by decompiling
// XUiC_DialogWindowGroup, XUiC_DialogResponseList, Dialog and DialogStatement:
//   - XUiC_DialogWindowGroup.Open(xui) opens the window group, and its OnOpen reads
//     xui.Dialog.Respondent, so the respondent must be set first.
//   - a response click is exactly: dialog.SelectResponse(response, player) followed by
//     dialogWindowGroup.RefreshDialog().
//
// Only meaningful on a game client - a dedicated server has no LocalPlayerUI.
internal static class VttDialogHarness
{
    public static void Execute(EntityPlayer player, List<string> args)
    {
        string sub = args.Count > 1 ? args[1].ToLowerInvariant() : string.Empty;

        switch (sub)
        {
            case "open":
                RunOpen(player, args.Count > 2 ? args[2] : null);
                break;
            case "dump":
                RunDump();
                break;
            case "select":
                RunSelect(player, args.Count > 2 ? args[2] : null);
                break;
            case "close":
                RunClose();
                break;
            case "seed":
                RunSeed(args.Count > 2 ? args[2] : null);
                break;
            default:
                VttTestHarness.Output(
                    "[vtttest] usage: vtttest dialog <open <traderEntityId>|seed <count>|dump|select <responseId>|close>");
                break;
        }
    }

    private static void RunOpen(EntityPlayer player, string traderEntityIdArg)
    {
        if (player == null || !int.TryParse(traderEntityIdArg, out int traderEntityId))
        {
            VttTestHarness.EmitResult("dialog.open", false, "a trader entity id is required (see 'le')");
            return;
        }

        if (!(GameManager.Instance?.World?.GetEntity(traderEntityId) is EntityTrader trader))
        {
            VttTestHarness.EmitResult("dialog.open", false, "trader entity not found");
            return;
        }

        LocalPlayerUI ui = LocalPlayerUI.GetUIForPrimaryPlayer();
        if (ui?.xui == null)
        {
            VttTestHarness.EmitResult("dialog.open", false, "no local player UI (dialog tests only run on a client)");
            return;
        }

        // XUiC_DialogWindowGroup.OnOpen dereferences xui.Dialog.Respondent, so it has to be set
        // before the window opens or the game throws inside its own open handler.
        ui.xui.Dialog.Respondent = trader;
        XUiC_DialogWindowGroup.Open(ui.xui);
        VttTestHarness.EmitResult("dialog.open", true, trader.EntityName);
    }

    private static void RunDump()
    {
        if (!TryGetWindowGroup(out XUiC_DialogWindowGroup windowGroup, out string error))
        {
            VttTestHarness.EmitResult("dialog.dump", false, error);
            return;
        }

        Dialog dialog = windowGroup.CurrentDialog;
        DialogStatement statement = dialog?.CurrentStatement;
        if (statement == null)
        {
            VttTestHarness.EmitResult("dialog.dump", false, "dialog has no current statement");
            return;
        }

        // The logical side: what the mod's GetResponses postfix actually produced.
        List<BaseResponseEntry> entries = statement.GetResponses();

        // The rendered side: how many of those the dialog skin actually has slots for. The
        // response list has a fixed number of XUiC_DialogResponseEntry children, and anything
        // past the last slot is silently dropped (XUiC_DialogResponseList.Update), which is
        // invisible unless the two counts are compared.
        var rendered = new List<string>();
        if (windowGroup.responseWindow?.entryList != null)
        {
            foreach (XUiC_DialogResponseEntry entry in windowGroup.responseWindow.entryList)
            {
                if (entry?.CurrentResponse != null)
                {
                    rendered.Add(entry.CurrentResponse.ID);
                }
            }
        }

        var json = new StringBuilder();
        json.Append("{\"statement\":").Append(Quote(statement.ID));
        json.Append(",\"statement_text\":").Append(Quote(statement.Text));
        json.Append(",\"language\":").Append(Quote(Localization.ActiveLanguage));
        json.Append(",\"entries\":[");
        for (int i = 0; i < entries.Count; i++)
        {
            DialogResponse response = entries[i].Response;
            if (i > 0)
            {
                json.Append(',');
            }

            json.Append("{\"id\":").Append(Quote(response?.ID));
            json.Append(",\"text\":").Append(Quote(response?.Text)).Append('}');
        }

        json.Append("],\"rendered\":[");
        for (int i = 0; i < rendered.Count; i++)
        {
            if (i > 0)
            {
                json.Append(',');
            }

            json.Append(Quote(rendered[i]));
        }

        json.Append("]}");

        // Its own marker so a driver can pull the structured dump out of the console output
        // without it colliding with the VTT_TEST_RESULT line below.
        VttTestHarness.Output("VTT_DIALOG_DUMP " + json);
        VttTestHarness.EmitResult("dialog.dump", true, $"{entries.Count} entries, {rendered.Count} rendered");
    }

    private static void RunSelect(EntityPlayer player, string responseId)
    {
        if (string.IsNullOrEmpty(responseId))
        {
            VttTestHarness.EmitResult("dialog.select", false, "a response id is required (see 'vtttest dialog dump')");
            return;
        }

        if (!TryGetWindowGroup(out XUiC_DialogWindowGroup windowGroup, out string error))
        {
            VttTestHarness.EmitResult("dialog.select", false, error);
            return;
        }

        Dialog dialog = windowGroup.CurrentDialog;
        DialogStatement statement = dialog?.CurrentStatement;
        if (statement == null)
        {
            VttTestHarness.EmitResult("dialog.select", false, "dialog has no current statement");
            return;
        }

        DialogResponse target = null;
        foreach (BaseResponseEntry entry in statement.GetResponses())
        {
            if (entry?.Response?.ID == responseId)
            {
                target = entry.Response;
                break;
            }
        }

        if (target == null)
        {
            VttTestHarness.EmitResult("dialog.select", false, $"no response '{responseId}' on statement '{statement.ID}'");
            return;
        }

        // Exactly what XUiC_DialogResponseList.OnPressResponse does for a real click.
        dialog.SelectResponse(target, player);
        windowGroup.RefreshDialog();
        VttTestHarness.EmitResult("dialog.select", true, responseId);
    }

    private static void RunClose()
    {
        LocalPlayerUI ui = LocalPlayerUI.GetUIForPrimaryPlayer();
        if (ui?.windowManager == null)
        {
            VttTestHarness.EmitResult("dialog.close", false, "no local player UI");
            return;
        }

        ui.windowManager.Close("dialog");
        VttTestHarness.EmitResult("dialog.close", true, "closed");
    }

    // Trader prefab names and biomes the synthetic destinations cycle through, so a seeded list
    // exercises the same localization lookups (trader name, biome name) and the same distance/
    // direction formatting a real one does, instead of placeholder strings that always render
    // the same width.
    private static readonly string[] SeedTraderNames =
    {
        "npcTraderBob", "npcTraderJen", "npcTraderHugh", "npcTraderJoel", "npcTraderRekt",
    };

    private static readonly string[] SeedBiomes =
    {
        "pine_forest", "desert", "snow", "wasteland", "burnt_forest",
    };

    // Replaces the client's destination list with <count> synthetic entries so paging can be
    // tested without visiting six traders across the map. Only the destination list is
    // replaced - access mode, travel cost and confirmation mode are carried over from the
    // snapshot the server actually sent, so cost lines and the confirmation screen still
    // behave as configured.
    //
    // ORDERING: DialogGetFirstStatementPatch.Postfix requests a fresh snapshot when the dialog
    // opens, and the reply overwrites whatever is seeded here. Seed AFTER 'dialog open', not
    // before, or the server's real (much shorter) list wins the race.
    private static void RunSeed(string countArg)
    {
        if (!int.TryParse(countArg, out int count) || count < 0 || count > 200)
        {
            VttTestHarness.EmitResult("dialog.seed", false, "a destination count between 0 and 200 is required");
            return;
        }

        if (!VisitedTraderNetwork.IsClientOnly)
        {
            VttTestHarness.EmitResult("dialog.seed", false, "seeding only applies to a client's snapshot cache");
            return;
        }

        var destinations = new List<TraderDestination>(count);
        for (int i = 0; i < count; i++)
        {
            string traderName = SeedTraderNames[i % SeedTraderNames.Length];
            // Spread the areas apart so nothing canonicalizes together and every entry gets a
            // distinct distance and compass direction in its label.
            int areaX = 900 + (i * 137);
            int areaZ = 950 - (i * 91);
            destinations.Add(new TraderDestination
            {
                Key = $"{traderName.ToLowerInvariant().Replace("npc", string.Empty)}:{areaX}:{areaZ}",
                DisplayName = traderName,
                Position = new Position3(areaX, 87f, areaZ),
                Forward = Position3.Forward,
                AreaX = areaX,
                AreaZ = areaZ,
                Biome = SeedBiomes[i % SeedBiomes.Length],
            });
        }

        VisitedTraderClientState.ApplySnapshot(
            VisitedTraderClientState.ServerAccessMode,
            destinations,
            VisitedTraderClientState.ServerTravelCost,
            VisitedTraderClientState.ServerConfirmation);

        VttTestHarness.EmitResult("dialog.seed", true, $"{count} destinations");
    }

    private static bool TryGetWindowGroup(out XUiC_DialogWindowGroup windowGroup, out string error)
    {
        windowGroup = null;
        LocalPlayerUI ui = LocalPlayerUI.GetUIForPrimaryPlayer();
        if (ui?.xui == null)
        {
            error = "no local player UI (dialog tests only run on a client)";
            return false;
        }

        windowGroup = ui.xui.Dialog?.DialogWindowGroup;
        if (windowGroup == null)
        {
            error = "no dialog is open (run 'vtttest dialog open <traderEntityId>' first)";
            return false;
        }

        error = null;
        return true;
    }

    // Minimal JSON string escaping. Response text carries the game's colour markup
    // (e.g. "[B0B0B0]...[-]") and localized text in every language, so it has to survive
    // quotes, backslashes and control characters intact.
    private static string Quote(string value)
    {
        if (value == null)
        {
            return "null";
        }

        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (c < ' ')
                    {
                        sb.Append("\\u").Append(((int)c).ToString("X4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}
#endif
