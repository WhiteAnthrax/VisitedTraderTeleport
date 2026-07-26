#if VTT_TEST_HARNESS
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace VisitedTraderTeleport;

// Test-only entry points that call the same production methods a real trader dialog
// interaction would (VisitedTraderStore.Record, DialogActionVisitedTraderTeleport.PerformAction),
// so a headless driver can exercise the mod's actual teleport code path without any UI.
internal static class VttTestHarness
{
    public static void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        EntityPlayer player = ResolvePlayer(_senderInfo);
        string sub = _params.Count > 0 ? _params[0].ToLowerInvariant() : string.Empty;

        switch (sub)
        {
            case "record":
                RunRecord(player, _params.ElementAtOrDefault(1));
                break;
            case "teleport":
                RunTeleport(player, _params.ElementAtOrDefault(1));
                break;
            case "list":
                RunList(player);
                break;
            default:
                Output("[vtttest] usage: vtttest <record <traderEntityId>|teleport <destinationKey>|list>");
                break;
        }
    }

    private static void RunRecord(EntityPlayer player, string traderEntityIdArg)
    {
        if (player == null || !int.TryParse(traderEntityIdArg, out int traderEntityId))
        {
            Output("[vtttest] record requires a trader entity id (see 'le' for nearby entity ids).");
            return;
        }

        if (!(GameManager.Instance?.World?.GetEntity(traderEntityId) is EntityTrader trader))
        {
            EmitResult("record", false, "trader entity not found");
            return;
        }

        // Same call DialogGetFirstStatementPatch.Postfix makes when a real dialog opens.
        VisitedTraderStore.Record(trader, player);
        EmitResult("record", true, ResolveRecordedKey(trader, player));
    }

    // VisitedTraderStore.Record can canonicalize the key it actually stores differently from
    // VisitedTraderStore.GetKey's raw form (widened with trader-area size on v2.6), so look up
    // the destination list afterwards and match by proximity to report a key a driver can
    // actually pass to 'vtttest teleport'. Falls back to the raw key if the canonicalized
    // destination isn't visible yet (e.g. a remote client whose snapshot hasn't caught up).
    private static string ResolveRecordedKey(EntityTrader trader, EntityPlayer player)
    {
        foreach (TraderDestination destination in VisitedTraderStore.GetDestinations(player))
        {
            if (Vector3.Distance(destination.Position, trader.position) < 5f)
            {
                return destination.Key;
            }
        }

        return VisitedTraderStore.GetKey(trader);
    }

    private static void RunTeleport(EntityPlayer player, string destinationKey)
    {
        if (player == null || string.IsNullOrEmpty(destinationKey))
        {
            Output("[vtttest] teleport requires a destination key (see 'vtttest list').");
            return;
        }

        // Same code path a real "Travel" dialog response click runs.
        new DialogActionVisitedTraderTeleport { Value = destinationKey }.PerformAction(player);
        EmitResult("teleport", true, destinationKey);
    }

    private static void RunList(EntityPlayer player)
    {
        if (player == null)
        {
            Output("[vtttest] list requires a resolvable player.");
            return;
        }

        IReadOnlyList<TraderDestination> destinations = VisitedTraderStore.GetDestinations(player);
        foreach (TraderDestination destination in destinations)
        {
            Output($"[vtttest] {destination.Key}\t{destination.DisplayName}");
        }

        EmitResult("list", true, $"{destinations.Count} destinations");
    }

    // A single-line JSON marker so an external driver (reading the Telnet stream or the log
    // file) can grep for the result of a vtttest command without parsing free-form text.
    private static void EmitResult(string action, bool ok, string detail)
    {
        Output($"VTT_TEST_RESULT {{\"action\":\"{action}\",\"ok\":{(ok ? "true" : "false")},\"detail\":\"{detail}\"}}");
    }

    private static void Output(string message)
    {
        Debug.Log(message);
        SdtdConsole.Instance.Output(message);
    }

    private static EntityPlayer ResolvePlayer(CommandSenderInfo senderInfo)
    {
        if (senderInfo.RemoteClientInfo != null)
        {
            return VisitedTraderNetwork.ResolvePlayer(senderInfo.RemoteClientInfo);
        }

        // Run from the host's own console (singleplayer, or the host's F1 console on a
        // player-hosted game) with no remote connection attached.
        return GameManager.Instance?.World?.GetPrimaryPlayer();
    }
}

// Opt-in gate so a Debug build never acts on vtttest unless someone deliberately drops the
// marker file next to the mod DLL. Debug builds should not ship, but this is a second layer
// in case one ends up on a live server anyway.
internal static class VttTestHarnessGate
{
    private const string MarkerFileName = "EnableTestHarness.txt";

    public static bool IsEnabled()
    {
        try
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, MarkerFileName));
        }
        catch
        {
            return false;
        }
    }
}
#endif
