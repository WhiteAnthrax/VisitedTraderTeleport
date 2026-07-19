using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VisitedTraderTeleport;

internal static class VisitedTraderTeleportService
{
    private const float TeleportVerticalClearance = 0.25f;
    private const float PrepareTimeoutSeconds = 12f;
    private const float TransitionVisualReadyMaxExtraSeconds = 15f;
    private const int PrepareChunkViewDim = 2;
    private const int ClientRefreshChunkViewDim = 3;
    private const float TravelCooldownSeconds = 10f;
    private const float MeshQueueBusyFraction = 0.8f;
    private const float ClientVisualRefreshMaxSeconds = 12f;
    private const float ClientVisualRefreshHoldSeconds = 5f;
    private const float ClientVisualRefreshArrivalDistanceSq = 64f * 64f;
    private const float TransitionArrivalLeadSeconds = 0.35f;
    private const float HiddenTransitionTeleportMaxDelaySeconds = 1.5f;
    private const float CompanionRecallRadius = 100f;

    private static readonly Dictionary<int, ChunkManager.ChunkObserver> PreparationObservers = new();
    private static readonly Dictionary<int, ChunkManager.ChunkObserver> ClientVisualRefreshObservers = new();
    private static readonly HashSet<int> PendingTeleports = new();
    private static readonly Dictionary<int, float> LastTravelTimes = new();

    public static void Teleport(EntityPlayer player, TraderDestination destination)
    {
        if (player == null || destination == null)
        {
            return;
        }

        int entityId = player.entityId;
        if (PendingTeleports.Contains(entityId))
        {
            return;
        }

        if (LastTravelTimes.TryGetValue(entityId, out float lastTravel))
        {
            float cooldownRemaining = TravelCooldownSeconds - (Time.realtimeSinceStartup - lastTravel);
            if (cooldownRemaining > 0f)
            {
                Debug.Log(
                    $"[VisitedTraderTeleport] Transport for {player.PlayerDisplayName} refused; " +
                    $"cooldown has {cooldownRemaining:0.#}s left.");
                ShowTooltip(player, VTTLocalization.Format("vtt_travel_cooldown", Mathf.CeilToInt(cooldownRemaining)));
                return;
            }
        }

        if (IsMeshQueueSaturated())
        {
            Debug.Log(
                $"[VisitedTraderTeleport] Transport for {player.PlayerDisplayName} deferred; " +
                "mesh regeneration queue is near its limit.");
            ShowTooltip(player, VTTLocalization.Get("vtt_transport_busy"));
            return;
        }

        Vector3 target = ResolveTarget(destination);
        if (player is EntityPlayerLocal && !TravelCostService.HasRequiredCost(player, destination))
        {
            return;
        }

        World world = GameManager.Instance?.World;
        if (NeedsPreparation(world, target, player is EntityPlayerLocal))
        {
            if (TryStartPreparedTransport(player, destination, target))
            {
                Debug.Log(
                    $"[VisitedTraderTeleport] Preparing destination for {player.PlayerDisplayName}: " +
                    $"{destination.DialogText}, target=({target.x:0.##}, {target.y:0.##}, {target.z:0.##}), " +
                    $"timeout={PrepareTimeoutSeconds:0.#}s.");
                ShowPreparingTooltip(player);
                return;
            }

            Debug.LogWarning(
                $"[VisitedTraderTeleport] Destination preparation could not start; transport blocked without charging " +
                $"{player.PlayerDisplayName}: {destination.DialogText}, " +
                $"target=({target.x:0.##}, {target.y:0.##}, {target.z:0.##}).");
            ShowDestinationNotReadyTooltip(player);
            return;
        }

        StartTransitionAndTeleport(player, destination, target, false);
    }

    public static void PrepareClientDestinationVisuals(EntityPlayerLocal player, TraderDestination destination)
    {
        if (player == null || destination == null)
        {
            return;
        }

        StartClientVisualRefresh(player, ResolveTarget(destination));
    }

    // Colliders (and the visual meshes that produce them) only exist on this instance for the
    // local player; a remote player's client checks its own arrival area, matching how the
    // dedicated-server path already worked. So only require colliders for a local traveler.
    private static bool IsDestinationReady(World world, Vector3 target, bool requireColliders)
    {
        if (world == null || !world.IsChunkAreaLoaded(target))
        {
            return false;
        }

        return GameManager.IsDedicatedServer || !requireColliders || world.IsChunkAreaCollidersLoaded(target);
    }

    private static bool NeedsPreparation(World world, Vector3 target, bool requireColliders)
    {
        return world != null && !IsDestinationReady(world, target, requireColliders);
    }

    // Mirrors the game's own throttle in ChunkManager.thread_Regenerating: when the number of
    // in-use VoxelMeshLayers reaches MaxQueuedMeshLayers, the mesh regeneration thread blocks.
    // Starting another map-wide trip in that state piles more work onto an already choking
    // queue, so refuse (without charging) while it is close to the limit.
    private static bool IsMeshQueueSaturated()
    {
        if (GameManager.IsDedicatedServer)
        {
            return false;
        }

        try
        {
            int queued = VoxelMeshLayer.InstanceCount - MemoryPools.poolVML.GetPoolSize();
            return queued >= ChunkManager.MaxQueuedMeshLayers * MeshQueueBusyFraction;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryStartPreparedTransport(EntityPlayer player, TraderDestination destination, Vector3 target)
    {
        GameManager gameManager = GameManager.Instance;
        World world = gameManager?.World;
        if (gameManager == null || world == null || player == null)
        {
            return false;
        }

        int entityId = player.entityId;
        if (PendingTeleports.Contains(entityId) || PreparationObservers.ContainsKey(entityId))
        {
            return true;
        }

        PendingTeleports.Add(entityId);
        gameManager.StartCoroutine(PrepareAndStartTransport(player, destination, target));
        return true;
    }

    private static IEnumerator PrepareAndStartTransport(EntityPlayer player, TraderDestination destination, Vector3 initialTarget)
    {
        GameManager gameManager = GameManager.Instance;
        World world = gameManager?.World;
        ChunkManager.ChunkObserver observer = null;
        int entityId = player?.entityId ?? -1;
        bool handedOffToTransition = false;

        try
        {
            // Build visual meshes only when the traveler is this instance's local player. For a
            // remote client the host only needs the chunk data loaded (the client renders its own
            // arrival area), and queueing host-side mesh work for someone else's destination was
            // feeding the mesh regeneration queue for nothing.
            bool localTraveler = player is EntityPlayerLocal;
            if (gameManager != null && world != null && player != null)
            {
                observer = gameManager.AddChunkObserver(
                    initialTarget,
                    localTraveler,
                    PrepareChunkViewDim,
                    player.entityId);
                PreparationObservers[player.entityId] = observer;
                if (localTraveler)
                {
                    ForceClientChunkVisualUpdate(world);
                }
            }

            float timeoutAt = Time.realtimeSinceStartup + PrepareTimeoutSeconds;
            while (player != null &&
                   world != null &&
                   !IsDestinationReady(world, initialTarget, localTraveler) &&
                   Time.realtimeSinceStartup < timeoutAt)
            {
                yield return null;
            }

            if (player == null || destination == null || world == null)
            {
                yield break;
            }

            Vector3 finalTarget = ResolveTarget(destination);
            if (!IsDestinationReady(world, finalTarget, localTraveler))
            {
                Debug.LogWarning(
                    $"[VisitedTraderTeleport] Destination was not ready after preparation; transport aborted without charging " +
                    $"{player.PlayerDisplayName}: {destination.DialogText}, " +
                    $"target=({finalTarget.x:0.##}, {finalTarget.y:0.##}, {finalTarget.z:0.##}).");
                ShowDestinationNotReadyTooltip(player);
                yield break;
            }

            Debug.Log(
                $"[VisitedTraderTeleport] Destination ready after preparation for {player.PlayerDisplayName}: " +
                $"{destination.DialogText}.");
            handedOffToTransition = StartTransitionAndTeleport(player, destination, finalTarget, false, true);
        }
        finally
        {
            if (gameManager != null && observer != null)
            {
                gameManager.RemoveChunkObserver(observer);
            }

            if (entityId >= 0)
            {
                PreparationObservers.Remove(entityId);
                if (!handedOffToTransition)
                {
                    PendingTeleports.Remove(entityId);
                }
            }
        }
    }

    private static void ExecuteTeleport(EntityPlayer player, TraderDestination destination, Vector3 target, bool showTooltip)
    {
        try
        {
            // A map-wide teleport respawns the player and unloads the chunk the companions are
            // standing in. SCore marks following hires as saved-to-file, so that departing chunk
            // would persist a copy that reloads as a duplicate when the player returns. Pull them
            // onto the (already prepared) destination first so the old chunk saves nothing.
            RelocateCompanionsBeforeTeleport(player, target);

            LastTravelTimes[player.entityId] = Time.realtimeSinceStartup;

            if (player is EntityPlayerLocal localPlayer)
            {
                // Teleport (not TeleportToPosition) avoids the respawn path that SCore /
                // XNPCCore hook to re-summon companions, which duplicated them every trip.
                // The respawn-style placement then shoves the player onto the roof of an indoor
                // trader POI (and it can fire a frame later), so hold the exact recorded floor
                // for a short while afterward to override it.
                localPlayer.Teleport(target, localPlayer.rotation.y);
                localPlayer.SetPosition(target, true);
                StartClientVisualRefresh(localPlayer, target);
                GameManager.Instance?.StartCoroutine(EnforceArrivalPosition(localPlayer, target));
            }
            else
            {
                player.Teleport(target, player.rotation.y);
                SendTeleportPackage(player, target);
                // On a server the remote player's local path (and our position hold) never runs,
                // so gather this player's companions to the destination here.
                GatherCompanions(player, target);
            }

            if (showTooltip && player is EntityPlayerLocal localForTooltip)
            {
                GameManager.ShowTooltip(localForTooltip, VTTLocalization.Format("vtt_teleported_to", TraderDestinationFormatter.FormatName(destination)), false, false, 4f);
            }

            Debug.Log($"[VisitedTraderTeleport] Teleported {player.PlayerDisplayName} to {destination.DialogText}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Teleport failed: {ex}");
        }
    }

    // Right after the teleport the spawn settling briefly resets the local player's position
    // (e.g. dropping the Y), which would otherwise leave the player off the trader floor. Hold
    // the exact recorded floor until the position stays put, then stop early so the hold is not
    // noticeable when the transition is off.
    private static IEnumerator EnforceArrivalPosition(EntityPlayerLocal player, Vector3 target)
    {
        const float maxSeconds = 3f;
        const float driftSqr = 0.75f * 0.75f;
        const int stableFramesNeeded = 12;
        float until = Time.realtimeSinceStartup + maxSeconds;
        int corrections = 0;
        int stableFrames = 0;

        while (player != null && !player.IsDead() &&
               Time.realtimeSinceStartup < until && stableFrames < stableFramesNeeded)
        {
            if ((player.position - target).sqrMagnitude > driftSqr)
            {
                corrections++;
                stableFrames = 0;
                player.SetPosition(target, true);
            }
            else
            {
                stableFrames++;
            }

            yield return null;
        }

        if (corrections > 0 && player != null)
        {
            Vector3 pos = player.position;
            Debug.Log(
                $"[VisitedTraderTeleport] Stabilized arrival at ({pos.x:0.##}, {pos.y:0.##}, {pos.z:0.##}) " +
                $"after {corrections} correction(s).");
        }

        GatherCompanions(player, player.position);
    }

    // SCore marks a following companion as saved-to-file (it has a "Leader" cvar) and respawnable
    // (bWillRespawn=true while following). A map-wide teleport respawns the player and unloads the
    // chunk the companion stands in; that chunk then persists a saved copy, which reloads as a
    // duplicate when the player returns to the area. Pulling the companions out of their current
    // chunk and onto the destination before the player jumps stops the departing chunk from saving
    // a copy. Scoped to SCore's own hired_<id> tracking on this player, and only moves entities
    // (never despawns), so other mods' owned entities are never touched. Stay/guard hires are left
    // in place, matching GatherCompanions.
    private static void RelocateCompanionsBeforeTeleport(EntityPlayer player, Vector3 target)
    {
        try
        {
            World world = GameManager.Instance?.World;
            if (player?.Buffs?.CVars == null || world == null)
            {
                return;
            }

            var ids = new List<int>();
            foreach (KeyValuePair<string, float> cvar in player.Buffs.CVars)
            {
                if (!cvar.Key.StartsWith("hired_", StringComparison.Ordinal))
                {
                    continue;
                }

                // The value normally holds the companion's entity id; a known SCore sync path
                // writes the id into the key as "hired_$<id>" with a zero value, so fall back to
                // parsing the key when the value is not usable.
                int id = (int)cvar.Value;
                if (id <= 0)
                {
                    int.TryParse(cvar.Key.Substring("hired_".Length).TrimStart('$'), out id);
                }

                if (id > 0)
                {
                    ids.Add(id);
                }
            }

            if (ids.Count == 0)
            {
                return;
            }

            int moved = 0;
            foreach (int id in ids)
            {
                if (!(world.GetEntity(id) is EntityAlive companion) || companion.IsDead())
                {
                    continue;
                }

                // A stale hired_ cvar can point at an entity id that was recycled onto something
                // else entirely (a parked vehicle, a zombie) after a save/load, which would drag
                // that entity to the destination. Require real ownership, same as GatherCompanions.
                if (!IsPlayerCompanion(companion, player.entityId))
                {
                    continue;
                }

                // Leave companions told to stay or guard where they are.
                if (IsStayingOrGuarding(id))
                {
                    continue;
                }

                if (companion.addedToChunk &&
                    world.GetChunkSync(companion.chunkPosAddedEntityTo.x, companion.chunkPosAddedEntityTo.z) is Chunk chunk)
                {
                    chunk.RemoveEntityFromChunk(companion);
                }

                ResetCompanionNavigation(companion);
                companion.SetPosition(target, true);
                moved++;
            }

            if (moved > 0)
            {
                Debug.Log($"[VisitedTraderTeleport] Pre-moved {moved} companion(s) out of the departing chunk before teleport.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Companion pre-move failed: {ex.Message}");
        }
    }

    // After the player is settled, pull their following NPC companions (e.g. SCore / XNPCCore
    // hires that came along) to the player so they are not left buried in the floor or scattered.
    // Companions are identified by ownership, so non-companion entities are left alone, and this
    // is a no-op on setups without companions.
    private static void GatherCompanions(EntityPlayer player, Vector3 center)
    {
        try
        {
            World world = GameManager.Instance?.World;
            if (player == null || world?.Entities?.list == null)
            {
                return;
            }

            EnsureOrderApiResolved();
            float radiusSqr = CompanionRecallRadius * CompanionRecallRadius;

            var companions = new List<EntityAlive>();
            foreach (Entity entity in new List<Entity>(world.Entities.list))
            {
                if (!(entity is EntityAlive alive) ||
                    alive.entityId == player.entityId ||
                    alive.IsDead() ||
                    !IsPlayerCompanion(alive, player.entityId))
                {
                    continue;
                }

                // Leave companions told to stay or guard where they are.
                if (IsStayingOrGuarding(alive.entityId))
                {
                    continue;
                }

                // When the order can't be read, only gather nearby companions so a stationed
                // one far away is not yanked along.
                if (!orderApiAvailable && (alive.position - center).sqrMagnitude > radiusSqr)
                {
                    continue;
                }

                companions.Add(alive);
            }

            for (int i = 0; i < companions.Count; i++)
            {
                Vector3 spot = FindCompanionSpot(world, center, i, companions.Count);
                ResetCompanionNavigation(companions[i]);
                companions[i].SetPosition(spot, true);
            }

            if (companions.Count > 0)
            {
                Debug.Log($"[VisitedTraderTeleport] Gathered {companions.Count} companion(s) around {player.PlayerDisplayName}.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Companion gather failed: {ex.Message}");
        }
    }

    // A spot in a ring around the player, on the player's floor level, avoiding solid blocks.
    // Falls back to tighter rings and finally the player's own position.
    private static Vector3 FindCompanionSpot(World world, Vector3 center, int index, int total)
    {
        float angle = total <= 0 ? 0f : (index / (float)total) * Mathf.PI * 2f;
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);
        foreach (float radius in new[] { 1.8f, 1.2f, 0.7f })
        {
            var spot = new Vector3(center.x + cos * radius, center.y, center.z + sin * radius);
            if (!IsBlockedAt(world, spot))
            {
                return spot;
            }
        }

        return center;
    }

    private static bool IsBlockedAt(World world, Vector3 pos)
    {
        int x = Mathf.FloorToInt(pos.x);
        int y = Mathf.FloorToInt(pos.y);
        int z = Mathf.FloorToInt(pos.z);
        return IsSolidBlock(world, x, y, z) || IsSolidBlock(world, x, y + 1, z);
    }

    private static bool IsSolidBlock(World world, int x, int y, int z)
    {
        try
        {
            Block block = world.GetBlock(x, y, z).Block;
            return block != null && block.shape != null && block.shape.IsSolidSpace;
        }
        catch
        {
            return false;
        }
    }

    // Mirrors SCore's own teleport bookkeeping so a repositioned companion does not freeze:
    // clear motion and the active path, and drop SCore's cached path for the entity.
    private static void ResetCompanionNavigation(EntityAlive companion)
    {
        try { companion.motion = Vector3.zero; }
        catch { /* ignore */ }

        try { companion.navigator?.clearPath(); }
        catch { /* ignore */ }

        try
        {
            EnsureRemovePathsResolved();
            removePathsMethod?.Invoke(null, new object[] { companion.entityId });
        }
        catch { /* SCore not present; ignore */ }
    }

    private static MethodInfo getCurrentOrderMethod;
    private static bool orderApiResolved;
    private static bool orderApiAvailable;

    private static void EnsureOrderApiResolved()
    {
        if (orderApiResolved)
        {
            return;
        }

        orderApiResolved = true;
        try
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType("EntityUtilities");
                if (type == null)
                {
                    continue;
                }

                getCurrentOrderMethod = type.GetMethod(
                    "GetCurrentOrder",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (getCurrentOrderMethod != null)
                {
                    orderApiAvailable = true;
                    break;
                }
            }
        }
        catch
        {
            // Optional; ignore when SCore is not present.
        }
    }

    // True when the companion is set to Stay or Guard, which should not be pulled along.
    private static bool IsStayingOrGuarding(int entityId)
    {
        if (!orderApiAvailable)
        {
            return false;
        }

        try
        {
            string order = getCurrentOrderMethod.Invoke(null, new object[] { entityId })?.ToString();
            return string.Equals(order, "Stay", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(order, "Guard", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static MethodInfo removePathsMethod;
    private static bool removePathsResolved;

    private static void EnsureRemovePathsResolved()
    {
        if (removePathsResolved)
        {
            return;
        }

        removePathsResolved = true;
        try
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType("SphereCache");
                if (type == null)
                {
                    continue;
                }

                removePathsMethod = type.GetMethod(
                    "RemovePaths",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (removePathsMethod != null)
                {
                    break;
                }
            }
        }
        catch
        {
            // Optional; ignore when SCore is not present.
        }
    }

    private static bool IsPlayerCompanion(EntityAlive alive, int playerId)
    {
        // Exclude other player-owned entities that are not following NPCs (e.g. the vanilla
        // junk drone, or any placed/drivable vehicle) so this stays a no-op outside of companion
        // setups. Checked by actual type, not by class name: every drivable vehicle (minibike,
        // motorcycle, bicycle, 4x4, gyrocopter, helicopter, blimp) derives from EntityVehicle via
        // EntityDriveable, but none of those concrete class names contain the substring "Vehicle" -
        // a prior name-substring check missed all of them.
        if (alive is EntityDrone || alive is EntityVehicle)
        {
            return false;
        }

        if (alive.belongsPlayerId == playerId)
        {
            return true;
        }

        EntityBuffs buffs = alive.Buffs;
        if (buffs == null)
        {
            return false;
        }

        return (buffs.HasCustomVar("Owner") && (int)buffs.GetCustomVar("Owner") == playerId) ||
               (buffs.HasCustomVar("Leader") && (int)buffs.GetCustomVar("Leader") == playerId);
    }

    private static bool StartTransitionAndTeleport(
        EntityPlayer player,
        TraderDestination destination,
        Vector3 target,
        bool costAlreadyConsumed,
        bool pendingAlreadySet = false)
    {
        TravelTransitionSettings settings = VisitedTraderTeleportConfig.TravelTransition;
        if (settings == null || !settings.Enabled || settings.DurationSeconds <= 0f)
        {
            if (!pendingAlreadySet && PendingTeleports.Contains(player.entityId))
            {
                return false;
            }

            if (!pendingAlreadySet)
            {
                PendingTeleports.Add(player.entityId);
            }

            if (!TryChargeTravelCost(player, destination, costAlreadyConsumed))
            {
                PendingTeleports.Remove(player.entityId);
                return false;
            }

            ExecuteTeleport(player, destination, target, true);
            PendingTeleports.Remove(player.entityId);
            return true;
        }

        GameManager gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            if (!pendingAlreadySet && PendingTeleports.Contains(player.entityId))
            {
                return false;
            }

            if (!pendingAlreadySet)
            {
                PendingTeleports.Add(player.entityId);
            }

            if (!TryChargeTravelCost(player, destination, costAlreadyConsumed))
            {
                PendingTeleports.Remove(player.entityId);
                return false;
            }

            ExecuteTeleport(player, destination, target, true);
            PendingTeleports.Remove(player.entityId);
            return true;
        }

        int entityId = player.entityId;
        if (!pendingAlreadySet && PendingTeleports.Contains(entityId))
        {
            return false;
        }

        if (!pendingAlreadySet)
        {
            PendingTeleports.Add(entityId);
        }

        if (!costAlreadyConsumed && player is EntityPlayerLocal && !TravelCostService.TryConsumeCost(player, destination, out int _))
        {
            PendingTeleports.Remove(entityId);
            return false;
        }

        gameManager.StartCoroutine(TransitionAndTeleport(player, destination, target, settings));
        return true;
    }

    // Charges the traveling player. The host/single-player consumes server-side; a remote
    // client is told to consume on its own client, independent of the transition visual.
    // Returns false only when a local player cannot pay (so the trip is aborted).
    private static bool TryChargeTravelCost(EntityPlayer player, TraderDestination destination, bool costAlreadyConsumed)
    {
        if (costAlreadyConsumed)
        {
            return true;
        }

        if (player is EntityPlayerLocal)
        {
            return TravelCostService.TryConsumeCost(player, destination, out int _);
        }

        SendRemoteTravelCostConsume(player, destination);
        return true;
    }

    private static void SendRemoteTravelCostConsume(EntityPlayer player, TraderDestination destination)
    {
        if (player == null || destination == null || player is EntityPlayerLocal)
        {
            return;
        }

        int paidCost = TravelCostService.CalculateCost(destination, player);
        string costItemName = VisitedTraderTeleportConfig.TravelCost?.ItemName ?? string.Empty;
        if (paidCost <= 0 || string.IsNullOrWhiteSpace(costItemName))
        {
            return;
        }

        ClientInfo clientInfo = ConnectionManager.Instance?.Clients?.ForEntityId(player.entityId);
        if (clientInfo == null)
        {
            Debug.LogWarning(
                $"[VisitedTraderTeleport] Could not charge travel cost for {player.PlayerDisplayName}; client not found.");
            return;
        }

        clientInfo.SendPackage(
            NetPackageManager.GetPackage<NetPackageVisitedTraderTravelTransition>()
                .Setup(
                    TraderDestinationFormatter.FormatName(destination),
                    TraderDestinationFormatter.FormatTransportDestination(destination),
                    paidCost,
                    costItemName,
                    TravelTransitionSettings.Disabled()));
        Debug.Log(
            $"[VisitedTraderTeleport] Sent travel cost charge to {player.PlayerDisplayName}: " +
            $"{paidCost} {costItemName} (transition off).");
    }

    private static IEnumerator TransitionAndTeleport(EntityPlayer player, TraderDestination destination, Vector3 target, TravelTransitionSettings settings)
    {
        int entityId = player?.entityId ?? -1;
        try
        {
            int paidCost = TravelCostService.CalculateCost(destination, player);
            string costItemName = VisitedTraderTeleportConfig.TravelCost?.ItemName ?? string.Empty;
            string destinationName = TraderDestinationFormatter.FormatName(destination);
            string transportDestination = TraderDestinationFormatter.FormatTransportDestination(destination);
            PlayTravelTransition(player, destinationName, transportDestination, paidCost, costItemName, settings);

            float teleportAt = Time.realtimeSinceStartup + GetTeleportDelay(settings);
            while (Time.realtimeSinceStartup < teleportAt)
            {
                yield return null;
            }

            if (player != null && destination != null)
            {
                ExecuteTeleport(player, destination, target, false);
            }

            float finishAt = Time.realtimeSinceStartup + GetTransitionHoldAfterTeleport(settings);
            while (Time.realtimeSinceStartup < finishAt)
            {
                yield return null;
            }
        }
        finally
        {
            if (entityId >= 0)
            {
                PendingTeleports.Remove(entityId);
            }
        }
    }

    public static void PlayClientTravelTransition(
        EntityPlayerLocal player,
        string destinationName,
        string transportDestination,
        int paidCost,
        TravelTransitionSettings settings)
    {
        if (player == null || settings == null || !settings.Enabled)
        {
            return;
        }

        GameManager.Instance?.StartCoroutine(ClientTravelTransition(player, destinationName, transportDestination, paidCost, settings));
    }

    private static IEnumerator ClientTravelTransition(
        EntityPlayerLocal player,
        string destinationName,
        string transportDestination,
        int paidCost,
        TravelTransitionSettings settings)
    {
        try
        {
            ApplyClientTransitionStart(player, transportDestination, paidCost, settings);

            float finishAt = Time.realtimeSinceStartup + Math.Max(0f, settings.DurationSeconds);
            float nextSoundAt = Time.realtimeSinceStartup + settings.SoundRepeatSeconds;
            while (Time.realtimeSinceStartup < finishAt)
            {
                if (settings.SoundRepeatSeconds > 0f && Time.realtimeSinceStartup >= nextSoundAt)
                {
                    PlayTravelSound(player, settings.Sound);
                    nextSoundAt = Time.realtimeSinceStartup + settings.SoundRepeatSeconds;
                }

                yield return null;
            }

            World destinationWorld = GameManager.Instance?.World;
            if (destinationWorld != null && player != null)
            {
                float visualReadyDeadline = Time.realtimeSinceStartup + TransitionVisualReadyMaxExtraSeconds;
                bool forcedRefresh = false;
                while (player != null && Time.realtimeSinceStartup < visualReadyDeadline)
                {
                    Vector3 here = player.position;
                    if (destinationWorld.IsChunkAreaLoaded(here) && destinationWorld.IsChunkAreaCollidersLoaded(here))
                    {
                        if (!forcedRefresh)
                        {
                            ForceClientChunkVisualUpdate(destinationWorld);
                            forcedRefresh = true;
                            yield return null;
                            continue;
                        }

                        break;
                    }

                    yield return null;
                }
            }

            if (!string.IsNullOrWhiteSpace(destinationName))
            {
                TravelTransitionOverlay.Hide();
                GameManager.ShowTooltip(player, VTTLocalization.Format("vtt_transport_arrival", destinationName), false, false, 4f);
            }
        }
        finally
        {
            TravelTransitionOverlay.Hide();
            ClearClientTransitionEffect(player, settings);
        }
    }

    private static void PlayTravelTransition(
        EntityPlayer player,
        string destinationName,
        string transportDestination,
        int paidCost,
        string costItemName,
        TravelTransitionSettings settings)
    {
        if (player is EntityPlayerLocal localPlayer)
        {
            PlayClientTravelTransition(localPlayer, destinationName, transportDestination, paidCost, settings);
            return;
        }

        try
        {
            ClientInfo clientInfo = ConnectionManager.Instance?.Clients?.ForEntityId(player.entityId);
            clientInfo?.SendPackage(
                NetPackageManager.GetPackage<NetPackageVisitedTraderTravelTransition>()
                    .Setup(destinationName, transportDestination, paidCost, costItemName, settings));
            Debug.Log(
                $"[VisitedTraderTeleport] Sending travel transition to {player.PlayerDisplayName}: " +
                $"paidCost={paidCost} {costItemName}, destination={destinationName}.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not send travel transition package: {ex.Message}");
        }
    }

    private static void ApplyClientTransitionStart(
        EntityPlayerLocal player,
        string destinationName,
        int paidCost,
        TravelTransitionSettings settings)
    {
        string message = paidCost > 0
            ? VTTLocalization.Format("vtt_transport_departure_paid", paidCost, GetEffectiveCostItemDisplayName(), destinationName)
            : VTTLocalization.Format("vtt_transport_departure", destinationName);
        TravelTransitionOverlay.Show(message);

        try
        {
            player.SetControllable(false);
            player.ClearMovementInputs();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not block player control for travel transition: {ex.Message}");
        }

        PlayTravelSound(player, settings.Sound);
    }

    private static void PlayTravelSound(EntityPlayerLocal player, string sound)
    {
        if (player == null || string.IsNullOrWhiteSpace(sound))
        {
            return;
        }

        string soundName = sound.Trim();
        bool foundSound = false;
        foreach (string candidate in GetSoundCandidates(soundName))
        {
            if (!IsKnownSound(candidate))
            {
                continue;
            }

            foundSound = true;
            PlayKnownTravelSound(player, candidate);
            return;
        }

        if (!foundSound)
        {
            Debug.LogWarning(
                $"[VisitedTraderTeleport] Travel sound '{soundName}' was not found in loaded audio data. " +
                "Try a sound key from the game's sounds.xml, or leave sound empty to disable it.");
        }
    }

    private static IEnumerable<string> GetSoundCandidates(string soundName)
    {
        yield return soundName;
        if (soundName.StartsWith("[", StringComparison.Ordinal) && soundName.EndsWith("]", StringComparison.Ordinal))
        {
            yield return soundName.Substring(1, soundName.Length - 2);
        }
        else
        {
            yield return "[" + soundName + "]";
        }
    }

    private static bool IsKnownSound(string soundName)
    {
        try
        {
            return Audio.Manager.audioData != null && Audio.Manager.audioData.ContainsKey(soundName);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not inspect loaded audio data for '{soundName}': {ex.Message}");
            return true;
        }
    }

    private static void PlayKnownTravelSound(EntityPlayerLocal player, string soundName)
    {
        Debug.Log($"[VisitedTraderTeleport] Playing travel sound '{soundName}'.");

        try
        {
            Audio.Manager.Play(player, soundName, 1f, false);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not play travel sound '{soundName}' on player entity: {ex.Message}");
        }

        try
        {
            Audio.Manager.PlayInsidePlayerHead(soundName, player.entityId);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not play travel sound '{soundName}' in player audio: {ex.Message}");
        }

        try
        {
            GameManager.Instance?.PlaySoundAtPositionClient(
                player.position,
                soundName,
                AudioRolloffMode.Linear,
                player.entityId);
            Audio.Manager.BroadcastPlayByLocalPlayer(player.position, soundName);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not play travel sound '{soundName}' at player position: {ex.Message}");
        }
    }

    private static string GetEffectiveCostItemDisplayName()
    {
        TravelCostSettings settings = VisitedTraderNetwork.IsClientOnly
            ? VisitedTraderClientState.ServerTravelCost
            : VisitedTraderTeleportConfig.TravelCost;
        return TravelCostService.FormatItemDisplayName(settings);
    }

    private static void ClearClientTransitionEffect(EntityPlayerLocal player, TravelTransitionSettings settings)
    {
        if (player == null)
        {
            return;
        }

        try
        {
            player.SetControllable(true);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not restore player control after travel transition: {ex.Message}");
        }
    }

    private static float GetTeleportDelay(TravelTransitionSettings settings)
    {
        float duration = Math.Max(0f, settings?.DurationSeconds ?? 0f);
        if (duration <= 0f)
        {
            return 0f;
        }

        return Math.Min(HiddenTransitionTeleportMaxDelaySeconds, duration * 0.35f);
    }

    private static float GetTransitionHoldAfterTeleport(TravelTransitionSettings settings)
    {
        float duration = Math.Max(0f, settings?.DurationSeconds ?? 0f);
        float hold = duration - GetTeleportDelay(settings);
        return Math.Max(TransitionArrivalLeadSeconds, hold);
    }

    private static void StartClientVisualRefresh(EntityPlayerLocal player, Vector3 target)
    {
        GameManager gameManager = GameManager.Instance;
        World world = gameManager?.World;
        if (player == null || gameManager == null || world == null)
        {
            return;
        }

        int entityId = player.entityId;
        if (ClientVisualRefreshObservers.TryGetValue(entityId, out ChunkManager.ChunkObserver existingObserver))
        {
            gameManager.RemoveChunkObserver(existingObserver);
            ClientVisualRefreshObservers.Remove(entityId);
        }

        gameManager.StartCoroutine(KeepClientDestinationVisualsLoaded(player, target));
    }

    private static IEnumerator KeepClientDestinationVisualsLoaded(EntityPlayerLocal player, Vector3 target)
    {
        GameManager gameManager = GameManager.Instance;
        World world = gameManager?.World;
        ChunkManager.ChunkObserver observer = null;
        int entityId = player?.entityId ?? -1;

        try
        {
            if (gameManager == null || world == null || player == null)
            {
                yield break;
            }

            observer = gameManager.AddChunkObserver(
                target,
                true,
                ClientRefreshChunkViewDim,
                player.entityId);
            ClientVisualRefreshObservers[player.entityId] = observer;
            ForceClientChunkVisualUpdate(world);

            float timeoutAt = Time.realtimeSinceStartup + ClientVisualRefreshMaxSeconds;
            float holdUntil = 0f;
            bool forcedAfterArrival = false;
            while (player != null && world != null && Time.realtimeSinceStartup < timeoutAt)
            {
                if (IsNearDestination(player, target) && IsDestinationReady(world, target, true))
                {
                    if (!forcedAfterArrival)
                    {
                        ForceClientChunkVisualUpdate(world);
                        forcedAfterArrival = true;
                    }

                    if (holdUntil <= 0f)
                    {
                        holdUntil = Time.realtimeSinceStartup + ClientVisualRefreshHoldSeconds;
                    }

                    if (Time.realtimeSinceStartup >= holdUntil)
                    {
                        break;
                    }
                }

                yield return null;
            }
        }
        finally
        {
            if (gameManager != null &&
                observer != null &&
                entityId >= 0 &&
                ClientVisualRefreshObservers.TryGetValue(entityId, out ChunkManager.ChunkObserver currentObserver) &&
                ReferenceEquals(currentObserver, observer))
            {
                gameManager.RemoveChunkObserver(observer);
                ClientVisualRefreshObservers.Remove(entityId);
            }
        }
    }

    private static bool IsNearDestination(EntityPlayerLocal player, Vector3 target)
    {
        if (player == null)
        {
            return false;
        }

        Vector3 delta = player.position - target;
        delta.y = 0f;
        return delta.sqrMagnitude <= ClientVisualRefreshArrivalDistanceSq;
    }

    private static void ForceClientChunkVisualUpdate(World world)
    {
        try
        {
            world?.m_ChunkManager?.ForceUpdate();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not force client chunk visual update: {ex.Message}");
        }
    }

    private static void ShowPreparingTooltip(EntityPlayer player)
    {
        // Remote clients already show their own localized "Preparing travel..." tooltip when
        // the dialog action fires, so only the local player needs this one.
        if (player is EntityPlayerLocal)
        {
            ShowTooltip(player, VTTLocalization.Get("vtt_preparing_travel"));
        }
    }

    private static void ShowDestinationNotReadyTooltip(EntityPlayer player)
    {
        ShowTooltip(player, VTTLocalization.Get("vtt_destination_not_ready"));
    }

    private static void ShowTooltip(EntityPlayer player, string message)
    {
        if (player is EntityPlayerLocal localPlayer)
        {
            GameManager.ShowTooltip(localPlayer, message, false, false, 4f);
        }
        else
        {
            // ShowTooltipMP's signature is (player, text, alertSound) - the message goes in the
            // second parameter. It used to be passed as the third, which sent an empty tooltip.
            GameManager.ShowTooltipMP(player, message);
        }
    }

    private static Vector3 ResolveTarget(TraderDestination destination)
    {
        Vector3 forward = destination.Forward;
        forward.y = 0f;
        Vector3 target = destination.Position;
        if (forward.sqrMagnitude >= 0.001f)
        {
            target += forward.normalized * 2f;
        }
        World world = GameManager.Instance?.World;
        if (world == null)
        {
            return target;
        }

        Vector3 clamped = world.ClampToValidWorldPos(target);
        // Trust the recorded floor height. GetHeightAt returns the top solid block, which is
        // the building roof for indoor traders, so raising to it would teleport onto the roof.
        // The old TeleportToPosition hid this by re-placing the player; a plain Teleport does not.
        clamped.y += TeleportVerticalClearance;
        return clamped;
    }

    private static void SendTeleportPackage(EntityPlayer player, Vector3 target)
    {
        try
        {
            ClientInfo clientInfo = ConnectionManager.Instance?.Clients?.ForEntityId(player.entityId);
            if (clientInfo == null)
            {
                return;
            }

            NetPackageTeleportPlayer package = NetPackageManager
                .GetPackage<NetPackageTeleportPlayer>()
                .Setup(target, null, false);
            clientInfo.SendPackage(package);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not send teleport package: {ex.Message}");
        }
    }
}
