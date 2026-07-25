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
    // Same size as the preparation observer. The old viewDim of 3 queued up to 49 chunk
    // columns of mesh work at once, which alone could push the mesh queue past the busy
    // threshold this service refuses trips at.
    private const int ClientRefreshChunkViewDim = 2;
    private const float ClientVisualRefreshMaxSeconds = 12f;
    private const float ClientVisualRefreshReleaseHoldSeconds = 1.5f;
    private const float QueuedTravelTimeoutSeconds = 45f;
    private const int MaxQueuedTravels = 3;
    private const float PreTeleportSaturationMaxWaitSeconds = 6f;
    private const float ClientVisualRefreshArrivalDistanceSq = 64f * 64f;
    private const float CompanionRecallRadius = 100f;

    private static readonly Dictionary<int, ChunkManager.ChunkObserver> PreparationObservers = new();
    private static readonly Dictionary<int, ChunkManager.ChunkObserver> ClientVisualRefreshObservers = new();
    private static readonly HashSet<int> PendingTeleports = new();
    private static readonly Dictionary<int, float> LastTravelTimes = new();
    private static readonly Queue<QueuedTravel> TravelQueue = new();
    private static bool travelSlotBusy;
    private static float travelSlotAcquiredAt;
    // Stamped fresh on every slot acquisition. The release coroutine and the watchdog compare
    // it before freeing the slot, so a delayed release from a superseded trip cannot free a
    // newer trip's slot.
    private static int travelSlotGeneration;
    private static bool queuedTravelTimeoutMonitorRunning;

    private sealed class QueuedTravel
    {
        public EntityPlayer Player;
        public TraderDestination Destination;
        public float QueuedAt;
    }

    public static void Teleport(EntityPlayer player, TraderDestination destination)
    {
        if (player == null || destination == null)
        {
            return;
        }

        if (!PassesStartChecks(player))
        {
            return;
        }

        // If the slot monitor coroutine died without its cleanup (e.g. the world it was
        // started in was unloaded), the slot would stay held forever. Force-release once the
        // hold is clearly past any legitimate trip length.
        if (travelSlotBusy &&
            Time.realtimeSinceStartup - travelSlotAcquiredAt > GetTravelSlotMaxHoldSeconds() + 10f)
        {
            Debug.LogWarning(
                "[VisitedTraderTeleport] Travel slot watchdog: the active trip never released the slot " +
                "(for example after a world change); cleaning up its leftover state and releasing it now.");
            // Releasing the slot alone would let the next trip start alongside the stuck trip's
            // leftover observers and pending flag. Tear those down first, then release, then pump
            // the queue: nothing else drains it on this path, so a trip waiting behind the stuck
            // one would otherwise stay queued forever.
            CleanupAllStaleTripState();
            // Advance the generation so the leaked trip's release coroutine, if it ever resumes
            // after this hitch, sees it no longer owns the slot and cannot free the trip that
            // ProcessTravelQueue is about to start.
            travelSlotGeneration++;
            travelSlotBusy = false;
            ProcessTravelQueue();
        }

        // The per-player cooldown cannot stop several players from starting a map-wide trip in
        // the same window, and each trip's chunk/mesh load lands on the queue asynchronously.
        // Run one trip at a time (preparation through arrival refresh); later requests wait in
        // a short queue instead of stacking their load onto the mesh pipeline at once.
        if (travelSlotBusy || TravelQueue.Count > 0)
        {
            EnqueueTravel(player, destination);
            return;
        }

        BeginTravel(player, destination);
    }

    // Checks shared by a fresh request and a queued trip about to start: both can be
    // invalidated while a request waits (another trip of the same player, a cooldown that
    // started in the meantime), so they run again at dequeue time.
    private static bool PassesStartChecks(EntityPlayer player)
    {
        int entityId = player.entityId;
        if (PendingTeleports.Contains(entityId))
        {
            return false;
        }

        if (LastTravelTimes.TryGetValue(entityId, out float lastTravel))
        {
            float cooldownRemaining = TravelCooldown.GetRemainingSeconds(Time.realtimeSinceStartup, lastTravel);
            if (cooldownRemaining > 0f)
            {
                Debug.Log(
                    $"[VisitedTraderTeleport] Transport for {player.PlayerDisplayName} refused; " +
                    $"cooldown has {cooldownRemaining:0.#}s left.");
                ShowTooltip(player, VTTLocalization.Format("vtt_travel_cooldown", Mathf.CeilToInt(cooldownRemaining)));
                return false;
            }
        }

        return true;
    }

    // A configured travel transition legitimately holds the pending flag for its full
    // duration, so the stuck-trip deadline has to sit above it.
    private static float GetTravelSlotMaxHoldSeconds()
    {
        return TravelCooldown.GetTravelSlotMaxHoldSeconds(VisitedTraderTeleportConfig.TravelTransition);
    }

    private static void EnqueueTravel(EntityPlayer player, TraderDestination destination)
    {
        foreach (QueuedTravel queued in TravelQueue)
        {
            if (queued.Player != null && queued.Player.entityId == player.entityId)
            {
                ShowTooltip(player, VTTLocalization.Get("vtt_transport_queued"));
                return;
            }
        }

        if (TravelQueue.Count >= MaxQueuedTravels)
        {
            Debug.Log(
                $"[VisitedTraderTeleport] Transport for {player.PlayerDisplayName} refused; " +
                $"travel queue is full ({TravelQueue.Count} waiting).");
            ShowTooltip(player, VTTLocalization.Get("vtt_transport_busy"));
            return;
        }

        TravelQueue.Enqueue(new QueuedTravel
        {
            Player = player,
            Destination = destination,
            QueuedAt = Time.realtimeSinceStartup
        });
        Debug.Log(
            $"[VisitedTraderTeleport] Transport for {player.PlayerDisplayName} queued behind the active trip " +
            $"({TravelQueue.Count} waiting).");
        ShowTooltip(player, VTTLocalization.Get("vtt_transport_queued"));
        EnsureQueuedTravelTimeoutMonitor();
    }

    // ProcessTravelQueue only runs when the active trip ends (up to the 60s+ slot safety cap),
    // so a waiting trip's QueuedTravelTimeoutSeconds deadline would not be checked until long
    // after it has passed. Watch the queue on its own so an expired entry is dropped and its
    // player notified the moment the deadline is reached, independent of the active trip.
    private static void EnsureQueuedTravelTimeoutMonitor()
    {
        if (queuedTravelTimeoutMonitorRunning)
        {
            return;
        }

        GameManager gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            return;
        }

        queuedTravelTimeoutMonitorRunning = true;
        gameManager.StartCoroutine(MonitorQueuedTravelTimeouts());
    }

    private static IEnumerator MonitorQueuedTravelTimeouts()
    {
        try
        {
            while (TravelQueue.Count > 0)
            {
                PurgeExpiredQueuedTravels();
                yield return null;
            }
        }
        finally
        {
            queuedTravelTimeoutMonitorRunning = false;
        }
    }

    // Drops every entry whose QueuedTravelTimeoutSeconds deadline has passed, notifying its
    // player, and preserves the order of the entries that remain.
    private static void PurgeExpiredQueuedTravels()
    {
        int count = TravelQueue.Count;
        for (int i = 0; i < count; i++)
        {
            QueuedTravel queued = TravelQueue.Dequeue();

            // An entry whose player has gone away can never start, so drop it outright instead
            // of re-queueing it (which would keep an un-startable, un-notifiable entry forever).
            if (queued.Player == null)
            {
                continue;
            }

            if (Time.realtimeSinceStartup - queued.QueuedAt > QueuedTravelTimeoutSeconds)
            {
                Debug.Log(
                    $"[VisitedTraderTeleport] Queued transport for {queued.Player.PlayerDisplayName} expired after " +
                    $"{QueuedTravelTimeoutSeconds:0.#}s; asking the player to retry.");
                ShowTooltip(queued.Player, VTTLocalization.Get("vtt_transport_busy"));
                continue;
            }

            TravelQueue.Enqueue(queued);
        }
    }

    private static void BeginTravel(EntityPlayer player, TraderDestination destination)
    {
        travelSlotBusy = true;
        travelSlotAcquiredAt = Time.realtimeSinceStartup;
        // Stamp this acquisition. Whoever releases the slot (the coroutine below, this method's
        // fallback, or a synchronous teleport) must prove it still holds this generation first.
        int generation = ++travelSlotGeneration;

        GameManager gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            try
            {
                TeleportCore(player, destination);
            }
            finally
            {
                ReleaseTravelSlotIfOwner(generation);
            }

            return;
        }

        bool monitoring = false;
        try
        {
            TeleportCore(player, destination);
            gameManager.StartCoroutine(ReleaseTravelSlotWhenTripComplete(player.entityId, generation));
            monitoring = true;
        }
        finally
        {
            if (!monitoring)
            {
                ReleaseTravelSlotIfOwner(generation);
            }
        }
    }

    // Frees the slot and pumps the queue only when `generation` still owns it. A trip that was
    // superseded (the watchdog force-released it and a newer trip took the slot, or advanced the
    // generation) must not free the current owner's slot nor start yet another trip beside it.
    private static void ReleaseTravelSlotIfOwner(int generation)
    {
        if (generation != travelSlotGeneration)
        {
            return;
        }

        travelSlotBusy = false;
        ProcessTravelQueue();
    }

    // The trip owns the travel slot from preparation until its observers are gone: the
    // preparation observer, the pending-teleport flag (covers the transition and the teleport
    // itself), and the local arrival-refresh observer. A refused trip sets none of these, so
    // the slot frees on the next frame. The deadline is a safety net against a stuck trip.
    private static IEnumerator ReleaseTravelSlotWhenTripComplete(int entityId, int generation)
    {
        try
        {
            float deadline = Time.realtimeSinceStartup + GetTravelSlotMaxHoldSeconds();
            while (Time.realtimeSinceStartup < deadline &&
                   (PendingTeleports.Contains(entityId) ||
                    PreparationObservers.ContainsKey(entityId) ||
                    ClientVisualRefreshObservers.ContainsKey(entityId)))
            {
                yield return null;
            }

            // Hitting the deadline means the trip's own cleanup never ran (a coroutine died
            // or an observer leaked). Releasing the slot with that state still in place would
            // let the next trip run concurrently with the stale one's load, which is exactly
            // what the slot exists to prevent - so tear the leftovers down first.
            if (PendingTeleports.Contains(entityId) ||
                PreparationObservers.ContainsKey(entityId) ||
                ClientVisualRefreshObservers.ContainsKey(entityId))
            {
                CleanupStaleTripState(entityId);
            }
        }
        finally
        {
            // Only release if this trip still owns the slot. If a long frame hitch delayed this
            // coroutine past the deadline, the Teleport watchdog may have already cleaned this
            // trip up, advanced the generation, and started the next trip - freeing the slot here
            // would strand that newer trip beside a third one pumped from the queue.
            ReleaseTravelSlotIfOwner(generation);
        }
    }

    private static void CleanupStaleTripState(int entityId)
    {
        Debug.LogWarning(
            $"[VisitedTraderTeleport] Trip for entity {entityId} did not finish within " +
            $"{GetTravelSlotMaxHoldSeconds():0.#}s; cleaning up its leftover state before releasing the travel slot.");

        GameManager gameManager = GameManager.Instance;

        // Each observer is torn down independently: if one RemoveChunkObserver throws, the other
        // observer and the pending flag must still be cleared, or the next trip would run
        // alongside exactly the leftover load this cleanup exists to remove.
        RemoveObserverSafely(gameManager, PreparationObservers, entityId, "preparation");
        RemoveObserverSafely(gameManager, ClientVisualRefreshObservers, entityId, "arrival-refresh");

        PendingTeleports.Remove(entityId);
    }

    // Watchdog path (Teleport's force-release): the leaked trip is not identified by a single
    // entity id here (its monitor coroutine died with the world), so clear every leftover
    // observer and pending flag before the next trip is allowed to start.
    private static void CleanupAllStaleTripState()
    {
        GameManager gameManager = GameManager.Instance;

        foreach (int entityId in new List<int>(PreparationObservers.Keys))
        {
            RemoveObserverSafely(gameManager, PreparationObservers, entityId, "preparation");
        }

        foreach (int entityId in new List<int>(ClientVisualRefreshObservers.Keys))
        {
            RemoveObserverSafely(gameManager, ClientVisualRefreshObservers, entityId, "arrival-refresh");
        }

        PendingTeleports.Clear();
    }

    private static void RemoveObserverSafely(
        GameManager gameManager,
        Dictionary<int, ChunkManager.ChunkObserver> observers,
        int entityId,
        string label)
    {
        try
        {
            if (observers.TryGetValue(entityId, out ChunkManager.ChunkObserver observer))
            {
                // Drop the entry before touching the game: a throw from RemoveChunkObserver must
                // not leave a stale entry that blocks the next trip, and clearing it first also
                // stops a still-alive coroutine (e.g. KeepClientDestinationVisualsLoaded) from
                // double-removing the same observer.
                observers.Remove(entityId);
                if (gameManager != null && observer != null)
                {
                    gameManager.RemoveChunkObserver(observer);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Stale {label} observer cleanup failed: {ex.Message}");
        }
    }

    private static void ProcessTravelQueue()
    {
        World world = GameManager.Instance?.World;
        while (!travelSlotBusy && TravelQueue.Count > 0)
        {
            QueuedTravel next = TravelQueue.Dequeue();
            EntityPlayer player = next.Player;
            if (player == null || player.IsDead() ||
                !(world?.GetEntity(player.entityId) is EntityPlayer))
            {
                continue;
            }

            if (Time.realtimeSinceStartup - next.QueuedAt > QueuedTravelTimeoutSeconds)
            {
                Debug.Log(
                    $"[VisitedTraderTeleport] Queued transport for {player.PlayerDisplayName} expired after " +
                    $"{QueuedTravelTimeoutSeconds:0.#}s; asking the player to retry.");
                ShowTooltip(player, VTTLocalization.Get("vtt_transport_busy"));
                continue;
            }

            // The player's state can change while the request waits, so the same checks a
            // fresh request goes through run again here.
            if (!PassesStartChecks(player))
            {
                continue;
            }

            Debug.Log($"[VisitedTraderTeleport] Starting queued transport for {player.PlayerDisplayName}.");
            try
            {
                BeginTravel(player, next.Destination);
            }
            catch (Exception ex)
            {
                // Keep pumping so one failed start does not strand the rest of the queue;
                // BeginTravel's own cleanup has already released the slot.
                Debug.LogWarning(
                    $"[VisitedTraderTeleport] Queued transport for {player.PlayerDisplayName} failed to start: {ex.Message}");
            }
        }
    }

    private static void TeleportCore(EntityPlayer player, TraderDestination destination)
    {
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
        if (world == null)
        {
            return false;
        }

        // Preserve the original short-circuit: IsChunkAreaCollidersLoaded is only queried
        // when it can actually change the result (not a dedicated server, colliders required).
        bool isDedicatedServer = GameManager.IsDedicatedServer;
        bool isCollidersLoaded = !isDedicatedServer && requireColliders && world.IsChunkAreaCollidersLoaded(target);
        return TravelReadinessChecks.IsDestinationReady(
            world.IsChunkAreaLoaded(target), isDedicatedServer, requireColliders, isCollidersLoaded);
    }

    private static bool NeedsPreparation(World world, Vector3 target, bool requireColliders)
    {
        return world != null && !IsDestinationReady(world, target, requireColliders);
    }

    private static bool IsMeshQueueSaturated()
    {
        if (GameManager.IsDedicatedServer)
        {
            return false;
        }

        try
        {
            int queued = VoxelMeshLayer.InstanceCount - MemoryPools.poolVML.GetPoolSize();
            return TravelReadinessChecks.IsMeshQueueSaturated(queued, ChunkManager.MaxQueuedMeshLayers);
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

            // Release this trip's own preparation observer before checking (and starting) the
            // transition. StartTransitionAndTeleport's first act is a mesh-queue saturation
            // check, and this observer's own PrepareChunkViewDim burst was still registered
            // (and its meshes still counted as in-flight) at that point, so the check was
            // measuring the load this same trip had just produced and refusing on its own tail
            // almost every time a destination genuinely needed preparation. Clear it here so the
            // finally block below is a no-op on this path, then give the queue a short bounded
            // grace period to actually drain (mirroring the wait TeleportAfterSaturationWait
            // uses elsewhere) instead of hard-refusing the instant the burst ends.
            if (gameManager != null && observer != null)
            {
                gameManager.RemoveChunkObserver(observer);
                observer = null;
            }

            if (entityId >= 0)
            {
                PreparationObservers.Remove(entityId);
            }

            float drainDeadline = Time.realtimeSinceStartup + PreTeleportSaturationMaxWaitSeconds;
            while (IsMeshQueueSaturated() && Time.realtimeSinceStartup < drainDeadline)
            {
                yield return null;
            }

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
        foreach (Position3 offset in CompanionSpotFinder.GetCandidateOffsets(index, total))
        {
            var spot = new Vector3(center.x + offset.X, center.y, center.z + offset.Z);
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
        // EntityDriveable, but none of those concrete class names contain the substring "Vehicle".
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
        // The saturation gate at request time is a single snapshot, and destination
        // preparation can take several seconds while chunk loading fills the mesh queue
        // asynchronously. Re-check here, before the trip is charged, so a request that
        // passed the first gate cannot start onto a queue that saturated in the meantime.
        // A trip whose cost is already consumed must not be refused (the player would be
        // charged for nothing); it falls through to the bounded pre-teleport wait instead.
        if (!costAlreadyConsumed && IsMeshQueueSaturated())
        {
            Debug.Log(
                $"[VisitedTraderTeleport] Transport for {player.PlayerDisplayName} deferred at start; " +
                "mesh regeneration queue saturated while the destination was being prepared.");
            ShowTooltip(player, VTTLocalization.Get("vtt_transport_busy"));
            return false;
        }

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

            // Cost is secured, so this trip is committed: pre-load the destination for the host's
            // own local player now (a no-op for a remote traveler and when preparation is already
            // loading it). Doing it after the charge means a trip refused for cost never pre-loads.
            EnsureLocalTravelerDestinationPreload(player, target);

            // Even with the transition off, the jump still has to clear the same bounded final
            // mesh-saturation wait the transition path uses. A cost-already-consumed trip skipped
            // the pre-charge refusal above, so without this it could jump straight onto a
            // saturated queue. Run the wait in a coroutine when a GameManager is available.
            GameManager immediateManager = GameManager.Instance;
            if (immediateManager != null)
            {
                immediateManager.StartCoroutine(TeleportAfterSaturationWait(player, destination, target));
                return true;
            }

            // No GameManager means no running World, so no chunk/mesh regeneration pipeline exists
            // to saturate (and no host to run a coroutine on). There is nothing to wait for, so the
            // missing saturation wait is correct here rather than a gap. Teleport directly.
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

            // No GameManager means no running World, hence no chunk/mesh regeneration pipeline
            // that could be saturated and no coroutine host, so there is nothing to wait for even
            // when the cost is already consumed. The direct teleport is correct on this path.
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

        // Cost is secured, so this trip is committed: pre-load the destination for the host's own
        // local player now (a no-op for a remote traveler and when preparation is already loading
        // it). Doing it after the charge means a trip refused for cost never pre-loads.
        EnsureLocalTravelerDestinationPreload(player, target);

        gameManager.StartCoroutine(TransitionAndTeleport(player, destination, target, settings));
        return true;
    }

    // Immediate (transition-off) teleport that still honors the bounded pre-teleport saturation
    // wait, mirroring the tail of TransitionAndTeleport. The cost is already charged by the
    // caller, so a saturated queue delays the jump (bounded) instead of refusing it. Clears the
    // pending flag when done.
    private static IEnumerator TeleportAfterSaturationWait(EntityPlayer player, TraderDestination destination, Vector3 target)
    {
        int entityId = player?.entityId ?? -1;
        try
        {
            float saturationDeadline = Time.realtimeSinceStartup + PreTeleportSaturationMaxWaitSeconds;
            while (IsMeshQueueSaturated() && Time.realtimeSinceStartup < saturationDeadline)
            {
                yield return null;
            }

            if (player != null && destination != null)
            {
                ExecuteTeleport(player, destination, target, true);
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

    // Single-player / P2P host only: pre-load the destination for the host's own (local) player,
    // standing in for the approval package a remote traveler would receive. A no-op for a remote
    // traveler (its package path handles it) and when preparation or an existing refresh is
    // already loading the destination, so it never stacks a second observer. The refresh
    // coroutine's own ClientVisualRefreshMaxSeconds bounds the load.
    private static void EnsureLocalTravelerDestinationPreload(EntityPlayer player, Vector3 target)
    {
        if (!(player is EntityPlayerLocal localPlayer))
        {
            return;
        }

        if (PreparationObservers.ContainsKey(localPlayer.entityId) ||
            ClientVisualRefreshObservers.ContainsKey(localPlayer.entityId))
        {
            return;
        }

        StartClientVisualRefresh(localPlayer, target);
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
            // Nothing to charge, but the package still has to go out: it is the client's
            // approval signal to start pre-loading its destination visuals.
            paidCost = 0;
            costItemName = string.Empty;
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
                    destination.Key,
                    TraderDestinationFormatter.FormatName(destination),
                    TraderDestinationFormatter.FormatTransportDestination(destination),
                    paidCost,
                    costItemName,
                    TravelTransitionSettings.Disabled()));
        Debug.Log(
            $"[VisitedTraderTeleport] Sent travel approval to {player.PlayerDisplayName}: " +
            $"cost={paidCost} {costItemName} (transition off).");
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
            PlayTravelTransition(player, destination.Key, destinationName, transportDestination, paidCost, costItemName, settings);

            float teleportAt = Time.realtimeSinceStartup + GetTeleportDelay(settings);
            while (Time.realtimeSinceStartup < teleportAt)
            {
                yield return null;
            }

            // Final check right before the teleport itself. The cost is already charged at
            // this point, so a saturated queue delays the jump (bounded) instead of refusing.
            float saturationDeadline = Time.realtimeSinceStartup + PreTeleportSaturationMaxWaitSeconds;
            while (IsMeshQueueSaturated() && Time.realtimeSinceStartup < saturationDeadline)
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
        string destinationKey,
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
                    .Setup(destinationKey, destinationName, transportDestination, paidCost, costItemName, settings));
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
        return TravelSoundCandidates.GetCandidates(soundName);
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
                player.entityId,
                1f);
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
        return TravelTransitionTiming.GetTeleportDelay(settings);
    }

    private static float GetTransitionHoldAfterTeleport(TravelTransitionSettings settings)
    {
        return TravelTransitionTiming.GetTransitionHoldAfterTeleport(settings);
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
            float releaseAt = 0f;
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

                    // The arrival meshes are built; keep the observer only long enough for
                    // the forced update to apply, then release it early instead of holding
                    // it for the rest of the refresh window.
                    if (releaseAt <= 0f)
                    {
                        releaseAt = Time.realtimeSinceStartup + ClientVisualRefreshReleaseHoldSeconds;
                    }

                    if (Time.realtimeSinceStartup >= releaseAt)
                    {
                        break;
                    }
                }

                yield return null;
            }
        }
        finally
        {
            // Re-fetch GameManager/World here instead of trusting the captured references
            // from coroutine start: a disconnect or world reload can tear both down while
            // this coroutine is still running (its own wait loop already re-checks world
            // each iteration, but that doesn't help a client-teardown that interrupts the
            // coroutine between iterations). Calling RemoveChunkObserver on a GameManager
            // whose World has already been cleaned up threw a NullReferenceException from
            // inside the game's own method and got the player kicked mid-teleport.
            GameManager currentGameManager = GameManager.Instance;
            if (currentGameManager != null &&
                currentGameManager.World != null &&
                observer != null &&
                entityId >= 0 &&
                ClientVisualRefreshObservers.TryGetValue(entityId, out ChunkManager.ChunkObserver currentObserver) &&
                ReferenceEquals(currentObserver, observer))
            {
                try
                {
                    currentGameManager.RemoveChunkObserver(observer);
                }
                catch (Exception ex)
                {
                    // Best-effort cleanup: the observer is being torn down along with the
                    // world/connection regardless, so a failure here must not propagate out
                    // of this finally block and take the client down with it.
                    Debug.LogWarning($"[VisitedTraderTeleport] Client visual refresh observer cleanup failed: {ex.Message}");
                }

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
        Vector3 forward = destination.Forward.ToVector3();
        forward.y = 0f;
        Vector3 target = destination.Position.ToVector3();
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
