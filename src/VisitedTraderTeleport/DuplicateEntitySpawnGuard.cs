using HarmonyLib;
using UnityEngine;

namespace VisitedTraderTeleport;

// Turns a duplicate entity spawn into a no-op instead of an exception.
//
// This mod loads the destination on the client while the travel overlay is up, so the area
// is rendered by the time the overlay lifts. The server streams those chunks - and the
// entities inside them - to the client at that point. Then the trip completes and the
// server's normal arrival handling sends the same area again, so the client is told to
// spawn entities it already has.
//
// World.SpawnEntityInWorld adds to a dictionary keyed by entity id, so the second attempt
// throws. The throw is not caught anywhere: it leaves EntityAsyncManager.Update through
// GameManager.gmUpdate, which aborts the rest of that frame's update and prints a red
// exception to the console. A player asked what the red text meant, which is how this was
// found.
//
// Skipping is not a behaviour change so much as a quieter route to the same outcome. The
// existing entity stays either way - the duplicate never replaces it, because the add is
// what fails. What skipping avoids is the aborted frame and the alarming log.
//
// It is deliberately not conditional on a trip being in flight. A duplicate spawn is never
// something the game wants: vanilla has no handler for it, which is why it surfaces as an
// unhandled exception. Dropping one quietly beats throwing it, wherever it comes from.
[HarmonyPatch(typeof(World), nameof(World.SpawnEntityInWorld))]
internal static class WorldSpawnEntityInWorldPatch
{
    // The guard runs on every entity spawn, so the log must not be able to flood. After this
    // many it goes quiet; the point of logging at all is to keep the skip visible rather
    // than to count them.
    private const int MaxLoggedSkips = 10;

    private static int loggedSkips;

    public static bool Prefix(World __instance, Entity _entity)
    {
        if (__instance == null || _entity == null)
        {
            return true;
        }

        // The raw dictionary, not World.GetEntity: that calls EntityAsyncManager.EnsureEntity
        // first, and this runs from inside the async manager's own completion path. A plain
        // lookup cannot re-enter it.
        if (!__instance.Entities.dict.ContainsKey(_entity.entityId))
        {
            return true;
        }

        if (loggedSkips < MaxLoggedSkips)
        {
            loggedSkips++;
            Debug.Log(
                $"[VisitedTraderTeleport] Entity {_entity.entityId} is already in the world; skipping the " +
                "duplicate spawn the game would have thrown on. This is expected right after travelling, " +
                "because the destination was already loaded for the arrival." +
                (loggedSkips == MaxLoggedSkips ? " Further skips will not be logged." : string.Empty));
        }

        return false;
    }
}
