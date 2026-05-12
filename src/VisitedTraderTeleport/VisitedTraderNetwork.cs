using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace VisitedTraderTeleport;

internal static class VisitedTraderNetwork
{
    private const int VisitReportPackageId = 240;
    private const int SnapshotRequestPackageId = 241;
    private const int SnapshotPackageId = 242;
    private const int TeleportRequestPackageId = 243;

    public static bool IsClientOnly
    {
        get
        {
            ConnectionManager manager = ConnectionManager.Instance;
            return manager != null && manager.IsClient && !manager.IsServer;
        }
    }

    public static void RegisterPackages()
    {
        RegisterPackage(VisitReportPackageId, typeof(NetPackageVisitedTraderVisitReport));
        RegisterPackage(SnapshotRequestPackageId, typeof(NetPackageVisitedTraderSnapshotRequest));
        RegisterPackage(SnapshotPackageId, typeof(NetPackageVisitedTraderSnapshot));
        RegisterPackage(TeleportRequestPackageId, typeof(NetPackageVisitedTraderTeleportRequest));
    }

    public static void ReportVisit(TraderVisitReport report)
    {
        if (!IsClientOnly || report == null)
        {
            return;
        }

        ConnectionManager.Instance.SendToServer(
            NetPackageManager.GetPackage<NetPackageVisitedTraderVisitReport>().Setup(report),
            false);
    }

    public static void RequestSnapshot()
    {
        if (!IsClientOnly)
        {
            return;
        }

        ConnectionManager.Instance.SendToServer(
            NetPackageManager.GetPackage<NetPackageVisitedTraderSnapshotRequest>().Setup(),
            false);
    }

    public static void RequestTeleport(string destinationKey)
    {
        if (!IsClientOnly || string.IsNullOrEmpty(destinationKey))
        {
            return;
        }

        ConnectionManager.Instance.SendToServer(
            NetPackageManager.GetPackage<NetPackageVisitedTraderTeleportRequest>().Setup(destinationKey),
            false);
    }

    public static void SendSnapshot(ClientInfo clientInfo)
    {
        EntityPlayer player = ResolvePlayer(clientInfo);
        if (clientInfo == null || player == null)
        {
            return;
        }

        IReadOnlyList<TraderDestination> destinations = VisitedTraderStore.GetDestinations(player);
        clientInfo.SendPackage(
            NetPackageManager.GetPackage<NetPackageVisitedTraderSnapshot>()
                .Setup(VisitedTraderTeleportConfig.AccessMode, destinations));
    }

    public static EntityPlayer ResolvePlayer(ClientInfo clientInfo)
    {
        if (clientInfo == null)
        {
            return null;
        }

        return GameManager.Instance?.World?.GetEntity(clientInfo.entityId) as EntityPlayer;
    }

    private static void RegisterPackage(int packageId, Type packageType)
    {
        try
        {
            NetPackageManager.AddPackageMapping(packageId, packageType);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not register net package {packageType.Name} ({packageId}): {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(NetPackageManager), nameof(NetPackageManager.SetupBaseMapping))]
internal static class NetPackageManagerSetupBaseMappingPatch
{
    public static void Postfix()
    {
        VisitedTraderNetwork.RegisterPackages();
    }
}
