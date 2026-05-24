using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VisitedTraderTeleport;

internal static class VisitedTraderTeleportService
{
    private const float TeleportVerticalClearance = 0.25f;
    private const float PrepareTimeoutSeconds = 8f;
    private const int PrepareChunkViewDim = 3;
    private const float ClientVisualRefreshMaxSeconds = 12f;
    private const float ClientVisualRefreshHoldSeconds = 5f;
    private const float ClientVisualRefreshArrivalDistanceSq = 64f * 64f;

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
        World world = GameManager.Instance?.World;
        if (!TravelCostService.HasRequiredCost(player, destination))
        {
            return;
        }

        if (NeedsPreparation(world, target) && TryStartPreparedTeleport(player, destination, target))
        {
            Debug.Log(
                $"[VisitedTraderTeleport] Preparing destination for {player.PlayerDisplayName}: " +
                $"{destination.DialogText}, target=({target.x:0.##}, {target.y:0.##}, {target.z:0.##}), " +
                $"timeout={PrepareTimeoutSeconds:0.#}s.");
            ShowPreparingTooltip(player);
            return;
        }

        StartTransitionAndTeleport(player, destination, target, false);
    }

    private static bool TryStartPreparedTeleport(EntityPlayer player, TraderDestination destination, Vector3 target)
    {
        GameManager gameManager = GameManager.Instance;
        World world = gameManager?.World;
        if (gameManager == null || world == null)
        {
            return false;
        }

        int entityId = player.entityId;
        if (PreparationObservers.ContainsKey(entityId))
        {
            return true;
        }

        gameManager.StartCoroutine(PrepareAndTeleport(player, destination, target));
        return true;
    }

    public static void PrepareClientDestinationVisuals(EntityPlayerLocal player, TraderDestination destination)
    {
        if (player == null || destination == null)
        {
            return;
        }

        StartClientVisualRefresh(player, ResolveTarget(destination));
    }

    private static IEnumerator PrepareAndTeleport(EntityPlayer player, TraderDestination destination, Vector3 initialTarget)
    {
        GameManager gameManager = GameManager.Instance;
        World world = gameManager?.World;
        ChunkManager.ChunkObserver observer = null;
        int entityId = player?.entityId ?? -1;

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
                    $"[VisitedTraderTeleport] Destination was not ready after preparation; teleport aborted for " +
                    $"{player.PlayerDisplayName}: {destination.DialogText}, " +
                    $"target=({finalTarget.x:0.##}, {finalTarget.y:0.##}, {finalTarget.z:0.##}).");
                ShowDestinationNotReadyTooltip(player);
                yield break;
            }

            if (player != null && destination != null)
            {
                Debug.Log(
                    $"[VisitedTraderTeleport] Destination ready after preparation for {player.PlayerDisplayName}: " +
                    $"{destination.DialogText}.");
                if (!TravelCostService.TryConsumeCost(player, destination, out int _))
                {
                    yield break;
                }

                StartTransitionAndTeleport(player, destination, finalTarget, true);
            }
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
            }
        }
    }

    private static bool NeedsPreparation(World world, Vector3 target)
    {
        return world != null && !IsDestinationReady(world, target);
    }

    private static bool IsDestinationReady(World world, Vector3 target)
    {
        if (world == null || !world.IsChunkAreaLoaded(target))
        {
            return false;
        }

        return GameManager.IsDedicatedServer || world.IsChunkAreaCollidersLoaded(target);
    }

    private static void ExecuteTeleport(EntityPlayer player, TraderDestination destination, Vector3 target)
    {
        try
        {
            if (player is EntityPlayerLocal localPlayer)
            {
                localPlayer.TeleportToPosition(target, false, null);
                StartClientVisualRefresh(localPlayer, target);
            }
            else
            {
                player.Teleport(target, player.rotation.y);
                SendTeleportPackage(player, target);
            }

            if (player is EntityPlayerLocal localForTooltip)
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

    private static void StartTransitionAndTeleport(EntityPlayer player, TraderDestination destination, Vector3 target, bool costAlreadyConsumed)
    {
        TravelTransitionSettings settings = VisitedTraderTeleportConfig.TravelTransition;
        if (settings == null || !settings.Enabled || settings.DurationSeconds <= 0f)
        {
            if (!costAlreadyConsumed && !TravelCostService.TryConsumeCost(player, destination, out int _))
            {
                return;
            }

            ExecuteTeleport(player, destination, target);
            return;
        }

        GameManager gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            if (!costAlreadyConsumed && !TravelCostService.TryConsumeCost(player, destination, out int _))
            {
                return;
            }

            ExecuteTeleport(player, destination, target);
            return;
        }

        int entityId = player.entityId;
        if (PendingTeleports.Contains(entityId))
        {
            return;
        }

        PendingTeleports.Add(entityId);
        if (!costAlreadyConsumed && !TravelCostService.TryConsumeCost(player, destination, out int _))
        {
            PendingTeleports.Remove(entityId);
            return;
        }

        gameManager.StartCoroutine(TransitionAndTeleport(player, destination, target, settings));
    }

    private static IEnumerator TransitionAndTeleport(EntityPlayer player, TraderDestination destination, Vector3 target, TravelTransitionSettings settings)
    {
        int entityId = player?.entityId ?? -1;
        try
        {
            int paidCost = TravelCostService.CalculateCost(destination, player);
            string destinationName = TraderDestinationFormatter.FormatName(destination);
            PlayTravelTransition(player, destinationName, paidCost, settings);

            float finishAt = Time.realtimeSinceStartup + settings.DurationSeconds;
            while (Time.realtimeSinceStartup < finishAt)
            {
                yield return null;
            }

            if (player != null && destination != null)
            {
                ExecuteTeleport(player, destination, target);
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
        int paidCost,
        TravelTransitionSettings settings)
    {
        if (player == null || settings == null || !settings.Enabled)
        {
            return;
        }

        GameManager.Instance?.StartCoroutine(ClientTravelTransition(player, destinationName, paidCost, settings));
    }

    private static IEnumerator ClientTravelTransition(
        EntityPlayerLocal player,
        string destinationName,
        int paidCost,
        TravelTransitionSettings settings)
    {
        try
        {
            ApplyClientTransitionStart(player, destinationName, paidCost, settings);

            float finishAt = Time.realtimeSinceStartup + Math.Max(0f, settings.DurationSeconds);
            while (Time.realtimeSinceStartup < finishAt)
            {
                yield return null;
            }

            if (!string.IsNullOrWhiteSpace(destinationName))
            {
                GameManager.ShowTooltip(player, VTTLocalization.Format("vtt_transport_arrival", destinationName), false, false, 4f);
            }
        }
        finally
        {
            ClearClientTransitionEffect(player, settings);
        }
    }

    private static void PlayTravelTransition(EntityPlayer player, string destinationName, int paidCost, TravelTransitionSettings settings)
    {
        if (player is EntityPlayerLocal localPlayer)
        {
            PlayClientTravelTransition(localPlayer, destinationName, paidCost, settings);
            return;
        }

        try
        {
            ClientInfo clientInfo = ConnectionManager.Instance?.Clients?.ForEntityId(player.entityId);
            clientInfo?.SendPackage(
                NetPackageManager.GetPackage<NetPackageVisitedTraderTravelTransition>()
                    .Setup(destinationName, paidCost, settings));
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
            ? VTTLocalization.Format("vtt_transport_departure_paid", paidCost, GetEffectiveCostItemDisplayName())
            : VTTLocalization.Get("vtt_transport_departure");
        GameManager.ShowTooltip(player, message, false, false, 3f);

        if (settings.DisableCamera)
        {
            try
            {
                player.EnableCamera(false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VisitedTraderTeleport] Could not disable camera for travel transition: {ex.Message}");
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.Sound))
        {
            try
            {
                if (Audio.Manager.CheckGlobalPlayRequirements(settings.Sound))
                {
                    GameManager.Instance.PlaySoundAtPositionClient(player.position, settings.Sound, AudioRolloffMode.Linear, player.entityId);
                }
                else
                {
                    Debug.LogWarning($"[VisitedTraderTeleport] Travel sound '{settings.Sound}' did not meet play requirements and was skipped.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VisitedTraderTeleport] Could not play travel sound '{settings.Sound}': {ex.Message}");
            }
        }
    }

    private static string GetEffectiveCostItemDisplayName()
    {
        TravelCostSettings settings = VisitedTraderNetwork.IsClientOnly
            ? VisitedTraderClientState.ServerTravelCost
            : VisitedTraderTeleportConfig.TravelCost;
        return string.IsNullOrWhiteSpace(settings.ItemDisplayName)
            ? settings.ItemName
            : settings.ItemDisplayName;
    }

    private static void ClearClientTransitionEffect(EntityPlayerLocal player, TravelTransitionSettings settings)
    {
        if (player == null || !settings.DisableCamera)
        {
            return;
        }

        try
        {
            player.EnableCamera(true);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not restore camera after travel transition: {ex.Message}");
        }
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
        if (player is EntityPlayerLocal localPlayer)
        {
            GameManager.ShowTooltip(localPlayer, VTTLocalization.Get("vtt_preparing_travel"), false, false, 2f);
        }
    }

    private static void ShowDestinationNotReadyTooltip(EntityPlayer player)
    {
        if (player is EntityPlayerLocal localPlayer)
        {
            GameManager.ShowTooltip(localPlayer, VTTLocalization.Get("vtt_destination_not_ready"), false, false, 4f);
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
