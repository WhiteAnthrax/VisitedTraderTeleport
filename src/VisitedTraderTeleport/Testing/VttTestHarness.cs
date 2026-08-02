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
            case "companions":
                RunCompanions(player, _params.ElementAtOrDefault(1));
                break;
            case "mark":
                RunMark(player, _params.ElementAtOrDefault(1), _params.ElementAtOrDefault(2),
                        _params.ElementAtOrDefault(3));
                break;
            case "dialog":
                VttDialogHarness.Execute(player, _params);
                break;
            default:
                Output("[vtttest] usage: vtttest <record <traderEntityId>|teleport <destinationKey>|list|" +
                       "companions|dialog <open <traderEntityId>|dump|select <responseId>|close>>");
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
    // VisitedTraderStore.GetKey's raw form (e.g. widening it with trader-area size), so look up
    // the destination list afterwards and match by proximity to report a key a driver can
    // actually pass to 'vtttest teleport'. Falls back to the raw key if the canonicalized
    // destination isn't visible yet (e.g. a remote client whose snapshot hasn't caught up).
    private static string ResolveRecordedKey(EntityTrader trader, EntityPlayer player)
    {
        foreach (TraderDestination destination in VisitedTraderStore.GetDestinations(player))
        {
            float dx = destination.Position.X - trader.position.x;
            float dy = destination.Position.Y - trader.position.y;
            float dz = destination.Position.Z - trader.position.z;
            if (dx * dx + dy * dy + dz * dz < 25f)
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

    // Puts one of the two markers the companion test reads onto an entity, so a scenario can
    // set up the situations that cannot otherwise be reached from a script.
    //
    //   hired - sets the "Owner" Buffs custom var to the player, which is exactly what SCore
    //           records when you hire an NPC (EntityUtilities.GetLeaderOrOwner reads it).
    //           Hiring for real needs NPC dialog, so there is no other headless route to a
    //           companion the mod will agree to gather.
    //   owned - sets belongsPlayerId to the player, which is what a turret gets when a player
    //           places one. Console-spawned turrets come out unowned, so without this the
    //           original bug's trigger condition cannot be reproduced at all.
    //
    // Test-only, and only reachable in a Debug build behind EnableTestHarness.txt. Nothing in
    // the shipped mod writes either marker.
    private static void RunMark(
        EntityPlayer player, string markerArg, string entityIdArg, string playerIdArg)
    {
        string marker = markerArg?.ToLowerInvariant();
        if (!int.TryParse(entityIdArg, out int entityId) ||
            (marker != "hired" && marker != "owned"))
        {
            Output("[vtttest] usage: vtttest mark <hired|owned> <entityId> [playerEntityId]");
            return;
        }

        // The player id is explicit when given, because this command has to be usable from the
        // *server* console - and that is where it matters most. Both markers are read by
        // server-side code (GatherCompanions), so writing them on the client marks a copy the
        // server never sees. There is no player context on a dedicated server console, so the
        // id cannot be resolved there and has to be passed in.
        int playerId;
        if (!string.IsNullOrEmpty(playerIdArg))
        {
            if (!int.TryParse(playerIdArg, out playerId))
            {
                Output("[vtttest] usage: vtttest mark <hired|owned> <entityId> [playerEntityId]");
                return;
            }
        }
        else if (player != null)
        {
            playerId = player.entityId;
        }
        else
        {
            EmitResult("mark", false, "no player context; pass the player entity id explicitly");
            return;
        }

        if (!(GameManager.Instance?.World?.GetEntity(entityId) is EntityAlive alive))
        {
            EmitResult("mark", false, "no living entity with that id");
            return;
        }

        if (marker == "owned")
        {
            alive.belongsPlayerId = playerId;
            EmitResult("mark", true, $"{entityId} belongsPlayerId={playerId}");
            return;
        }

        if (alive.Buffs == null)
        {
            EmitResult("mark", false, "entity has no Buffs to write the Owner var to");
            return;
        }

        alive.Buffs.SetCustomVar("Owner", playerId);
        EmitResult("mark", true, $"{entityId} Owner={playerId}");
    }

    // Reports what IsPlayerCompanion decides about every live entity, next to the raw markers
    // it decided from. Read-only.
    //
    // This exists because the companion test has now been wrong twice in the same way, and
    // both times the wrongness was invisible from outside: an owned turret or vehicle was
    // silently classed as a companion and only showed up as "my turret moved". Printing the
    // markers alongside the verdict makes the next disagreement a five-second check.
    //
    // "would_match_ownership" is what the old rule said - belongsPlayerId == playerId. Where
    // it is true and companion is false, this entity is one the old code would have dragged
    // along.
    private static void RunCompanions(EntityPlayer player, string playerIdArg)
    {
        // Same reason `mark` takes one: this is worth asking on the *server*, where the
        // decision is actually made, and a dedicated server console has no player context.
        // Running it on both sides and comparing is how a marker written to the wrong copy
        // shows up immediately.
        int playerId;
        if (!string.IsNullOrEmpty(playerIdArg))
        {
            if (!int.TryParse(playerIdArg, out playerId))
            {
                Output("[vtttest] usage: vtttest companions [playerEntityId]");
                return;
            }
        }
        else if (player != null)
        {
            playerId = player.entityId;
        }
        else
        {
            EmitResult("companions", false, "no player context; pass the player entity id explicitly");
            return;
        }

        World world = GameManager.Instance?.World;
        if (world?.Entities?.list == null)
        {
            EmitResult("companions", false, "no world entity list");
            return;
        }

        int reported = 0;
        int companions = 0;
        int wouldHaveMatchedOwnership = 0;
        foreach (Entity entity in new List<Entity>(world.Entities.list))
        {
            if (!(entity is EntityAlive alive) || alive.entityId == playerId)
            {
                continue;
            }

            EntityBuffs buffs = alive.Buffs;
            string leader = buffs != null && buffs.HasCustomVar("Leader")
                ? ((int)buffs.GetCustomVar("Leader")).ToString()
                : "-";
            string owner = buffs != null && buffs.HasCustomVar("Owner")
                ? ((int)buffs.GetCustomVar("Owner")).ToString()
                : "-";

            bool isCompanion = VisitedTraderTeleportService.IsPlayerCompanion(alive, playerId);
            bool ownershipMatch = alive.belongsPlayerId == playerId;

            if (isCompanion)
            {
                companions++;
            }

            if (ownershipMatch)
            {
                wouldHaveMatchedOwnership++;
            }

            Output(
                "VTT_COMPANION_PROBE {" +
                $"\"entity_id\":{alive.entityId}," +
                $"\"type\":\"{alive.GetType().Name}\"," +
                $"\"name\":\"{alive.EntityName}\"," +
                $"\"belongs_player_id\":{alive.belongsPlayerId}," +
                $"\"leader\":\"{leader}\"," +
                $"\"owner\":\"{owner}\"," +
                $"\"companion\":{(isCompanion ? "true" : "false")}," +
                $"\"would_match_ownership\":{(ownershipMatch ? "true" : "false")}" +
                "}");
            reported++;
        }

        EmitResult(
            "companions",
            true,
            $"{reported} entities, {companions} companion(s), {wouldHaveMatchedOwnership} owned-by-player");
    }

    // A single-line JSON marker so an external driver (reading the Telnet stream or the log
    // file) can grep for the result of a vtttest command without parsing free-form text.
    internal static void EmitResult(string action, bool ok, string detail)
    {
        Output($"VTT_TEST_RESULT {{\"action\":\"{action}\",\"ok\":{(ok ? "true" : "false")},\"detail\":\"{detail}\"}}");
    }

    internal static void Output(string message)
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
