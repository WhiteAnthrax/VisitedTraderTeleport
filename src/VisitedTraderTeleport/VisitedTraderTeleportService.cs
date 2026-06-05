using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace VisitedTraderTeleport;

internal static class VisitedTraderTeleportService
{
    private const float TeleportVerticalClearance = 0.25f;
    private const float PrepareTimeoutSeconds = 12f;
    private const float TransitionVisualReadyMaxExtraSeconds = 15f;
    private const int PrepareChunkViewDim = 3;
    private const float ClientVisualRefreshMaxSeconds = 12f;
    private const float ClientVisualRefreshHoldSeconds = 5f;
    private const float ClientVisualRefreshArrivalDistanceSq = 64f * 64f;
    private const float TransitionArrivalLeadSeconds = 0.35f;
    private const float HiddenTransitionTeleportMaxDelaySeconds = 1.5f;
    private const float CompanionRecallRadius = 100f;

    private static readonly Dictionary<int, ChunkManager.ChunkObserver> PreparationObservers = new();
    private static readonly Dictionary<int, ChunkManager.ChunkObserver> ClientVisualRefreshObservers = new();
    private static readonly HashSet<int> PendingTeleports = new();

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

        Vector3 target = ResolveTarget(destination);
        if (player is EntityPlayerLocal && !TravelCostService.HasRequiredCost(player, destination))
        {
            return;
        }

        World world = GameManager.Instance?.World;
        if (NeedsPreparation(world, target))
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

    private static bool IsDestinationReady(World world, Vector3 target)
    {
        if (world == null || !world.IsChunkAreaLoaded(target))
        {
            return false;
        }

        return GameManager.IsDedicatedServer || world.IsChunkAreaCollidersLoaded(target);
    }

    private static bool NeedsPreparation(World world, Vector3 target)
    {
        return world != null && !IsDestinationReady(world, target);
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
            if (gameManager != null && world != null && player != null)
            {
                observer = gameManager.AddChunkObserver(
                    initialTarget,
                    !GameManager.IsDedicatedServer,
                    PrepareChunkViewDim,
                    player.entityId);
                PreparationObservers[player.entityId] = observer;
                if (!GameManager.IsDedicatedServer)
                {
                    ForceClientChunkVisualUpdate(world);
                }
            }

            float timeoutAt = Time.realtimeSinceStartup + PrepareTimeoutSeconds;
            while (player != null &&
                   world != null &&
                   !IsDestinationReady(world, initialTarget) &&
                   Time.realtimeSinceStartup < timeoutAt)
            {
                yield return null;
            }

            if (player == null || destination == null || world == null)
            {
                yield break;
            }

            Vector3 finalTarget = ResolveTarget(destination);
            if (!IsDestinationReady(world, finalTarget))
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

        RecallFollowingCompanions(player);
    }

    // After the player is settled, pull their following NPC companions (e.g. SCore / XNPCCore
    // hires that came along) to the player so they are not left buried in the floor or scattered.
    // Companions are identified by ownership, so non-companion entities are left alone, and this
    // is a no-op on setups without companions.
    private static void RecallFollowingCompanions(EntityPlayerLocal player)
    {
        try
        {
            World world = GameManager.Instance?.World;
            if (player == null || world?.Entities?.list == null)
            {
                return;
            }

            Vector3 center = player.position;
            float radiusSqr = CompanionRecallRadius * CompanionRecallRadius;
            int viaScore = 0;
            int viaFallback = 0;
            int skipped = 0;

            foreach (Entity entity in new List<Entity>(world.Entities.list))
            {
                if (!(entity is EntityAlive alive) ||
                    alive.entityId == player.entityId ||
                    alive.IsDead() ||
                    !IsPlayerCompanion(alive, player.entityId) ||
                    (alive.position - center).sqrMagnitude > radiusSqr)
                {
                    continue;
                }

                LogCompanionApiOnce(alive);

                // Prefer SCore's own companion-to-leader teleport so its AI/leader bookkeeping
                // runs (a plain SetPosition leaves SDX companions stuck). The cooldown lives in
                // the separate validateTeleport check, so calling this directly is not throttled.
                if (TryScoreTeleport(alive, player, out bool hadScoreMethod))
                {
                    viaScore++;
                }
                else if (!hadScoreMethod)
                {
                    // No SCore teleport method (non-SCore companion); a plain reposition is fine.
                    alive.SetPosition(center + CompanionRecallOffset(viaFallback), true);
                    viaFallback++;
                }
                else
                {
                    // The method exists but the call failed; leave the companion where it is
                    // rather than freezing it with a raw SetPosition.
                    skipped++;
                }
            }

            if (viaScore + viaFallback + skipped > 0)
            {
                Debug.Log(
                    $"[VisitedTraderTeleport] Recalled companions: SCore call={viaScore}, " +
                    $"fallback={viaFallback}, skipped={skipped}.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Companion recall failed: {ex.Message}");
        }
    }

    // Calls SCore's EntityAliveSDX.TeleportToPlayer(EntityAlive leader, bool) (or a similar
    // companion-to-leader teleport) by reflection. hadMethod reports whether such a method was
    // found, so the caller can decide whether a raw reposition fallback is appropriate.
    private static bool TryScoreTeleport(EntityAlive companion, EntityAlive leader, out bool hadMethod)
    {
        hadMethod = false;
        try
        {
            Type type = companion.GetType();
            foreach (string name in new[] { "TeleportToPlayer", "TeleportToLeader" })
            {
                MethodInfo method = type.GetMethod(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (method == null)
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                object[] args;
                if (parameters.Length == 0)
                {
                    args = null;
                }
                else if (parameters.Length == 2 &&
                         parameters[0].ParameterType.IsInstanceOfType(leader) &&
                         parameters[1].ParameterType == typeof(bool))
                {
                    // false = move the existing companion only; true appeared to spawn a copy.
                    args = new object[] { leader, false };
                }
                else if (parameters.Length == 1 &&
                         parameters[0].ParameterType.IsInstanceOfType(leader))
                {
                    args = new object[] { leader };
                }
                else
                {
                    continue;
                }

                hadMethod = true;
                method.Invoke(companion, args);
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] SCore companion teleport failed: {ex.Message}");
        }

        return false;
    }

    private static bool loggedCompanionApi;

    // One-shot diagnostic: log the companion entity type and its teleport/leader-related methods
    // (with parameter types) so the exact SCore method to call can be confirmed from the log.
    private static void LogCompanionApiOnce(EntityAlive companion)
    {
        if (loggedCompanionApi || companion == null)
        {
            return;
        }

        loggedCompanionApi = true;
        try
        {
            Type type = companion.GetType();
            var builder = new StringBuilder();
            foreach (MethodInfo method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                string name = method.Name;
                if (name.IndexOf("Teleport", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("Warp", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("Leader", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                builder.Append(name).Append('(');
                ParameterInfo[] parameters = method.GetParameters();
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    builder.Append(parameters[i].ParameterType.Name);
                }

                builder.Append(") ");
            }

            Debug.Log($"[VisitedTraderTeleport] Companion type {type.FullName}; methods: {builder}");
        }
        catch
        {
            // Diagnostic only; ignore.
        }
    }

    private static bool IsPlayerCompanion(EntityAlive alive, int playerId)
    {
        // Exclude other player-owned entities that are not following NPCs (e.g. the vanilla
        // junk drone) so this stays a no-op outside of companion setups.
        string typeName = alive.GetType().Name;
        if (typeName.IndexOf("Drone", StringComparison.OrdinalIgnoreCase) >= 0 ||
            typeName.IndexOf("Vehicle", StringComparison.OrdinalIgnoreCase) >= 0)
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

    private static Vector3 CompanionRecallOffset(int index)
    {
        // Spread companions in a small ring so they do not stack on the player.
        float angle = index * 1.3f;
        float radius = 1.5f + 0.35f * index;
        return new Vector3(Mathf.Cos(angle) * radius, 0.1f, Mathf.Sin(angle) * radius);
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
                PrepareChunkViewDim,
                player.entityId);
            ClientVisualRefreshObservers[player.entityId] = observer;
            ForceClientChunkVisualUpdate(world);

            float timeoutAt = Time.realtimeSinceStartup + ClientVisualRefreshMaxSeconds;
            float holdUntil = 0f;
            bool forcedAfterArrival = false;
            while (player != null && world != null && Time.realtimeSinceStartup < timeoutAt)
            {
                if (IsNearDestination(player, target) && IsDestinationReady(world, target))
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
        ShowTooltip(player, VTTLocalization.Get("vtt_preparing_travel"));
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
            GameManager.ShowTooltipMP(player, string.Empty, message);
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
