using System;
using UnityEngine;

namespace VisitedTraderTeleport;

internal static class VisitedTraderTeleportService
{
    private const float TeleportVerticalClearance = 0.25f;

    public static void Teleport(EntityPlayer player, TraderDestination destination)
    {
        if (player == null || destination == null)
        {
            return;
        }

        Vector3 target = ResolveTarget(destination);

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
        float terrainY = world.GetHeightAt(clamped.x, clamped.z) + 1.0f;
        if (!float.IsNaN(terrainY) && terrainY > clamped.y)
        {
            clamped.y = terrainY;
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
