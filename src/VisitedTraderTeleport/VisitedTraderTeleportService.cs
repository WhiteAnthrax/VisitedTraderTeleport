using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VisitedTraderTeleport;

internal static class VisitedTraderTeleportService
{
    private const float TeleportVerticalClearance = 0.25f;
    private const float PrepareTimeoutSeconds = 4f;
    private const int PrepareChunkViewDim = 3;

    private static readonly Dictionary<int, ChunkManager.ChunkObserver> PreparationObservers = new();

    public static void Teleport(EntityPlayer player, TraderDestination destination)
    {
        if (player == null || destination == null)
        {
            return;
        }

        Vector3 target = ResolveTarget(destination);
        World world = GameManager.Instance?.World;
        if (NeedsPreparation(world, target) && TryStartPreparedTeleport(player, destination, target))
        {
            ShowPreparingTooltip(player);
            return;
        }

        ExecuteTeleport(player, destination, target);
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

            if (player != null && destination != null)
            {
                ExecuteTeleport(player, destination, ResolveTarget(destination));
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
            }
            else
            {
                player.Teleport(target, player.rotation.y);
                SendTeleportPackage(player, target);
            }

            if (player is EntityPlayerLocal localForTooltip)
            {
                GameManager.ShowTooltip(localForTooltip, VTTLocalization.Format("vtt_teleported_to", destination.DisplayName), false, false, 4f);
            }

            Debug.Log($"[VisitedTraderTeleport] Teleported {player.PlayerDisplayName} to {destination.DialogText}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Teleport failed: {ex}");
        }
    }

    private static void ShowPreparingTooltip(EntityPlayer player)
    {
        if (player is EntityPlayerLocal localPlayer)
        {
            GameManager.ShowTooltip(localPlayer, VTTLocalization.Get("vtt_preparing_travel"), false, false, 2f);
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
