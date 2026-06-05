using System;
using System.Collections;
using System.Collections.Generic;
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
                // Use a plain Teleport rather than TeleportToPosition so this does not fire a
                // respawn-style PlayerSpawnedInWorld event. Companion frameworks (SCore /
                // XNPCCore) hook that event and re-summon companions, duplicating them every
                // trip. The destination chunks are already preloaded by the preparation step,
                // so arrival stays safe.
                localPlayer.Teleport(target, localPlayer.rotation.y);
                StartClientVisualRefresh(localPlayer, target);
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
        if (world.IsChunkAreaLoaded(clamped))
        {
            float terrainY = world.GetHeightAt(clamped.x, clamped.z) + 1.0f;
            if (!float.IsNaN(terrainY) && terrainY > clamped.y)
            {
                clamped.y = terrainY;
            }
        }

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
