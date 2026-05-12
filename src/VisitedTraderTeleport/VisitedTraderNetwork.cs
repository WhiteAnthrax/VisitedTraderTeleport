using System;
using System.Collections.Generic;

namespace VisitedTraderTeleport;

internal static class VisitedTraderNetwork
{
    public static bool IsClientOnly
    {
        get
        {
            ConnectionManager manager = ConnectionManager.Instance;
            return manager != null && manager.IsClient && !manager.IsServer;
        }
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
}
