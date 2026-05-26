using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VisitedTraderTeleport;

public sealed class ModApi : IModApi
{
    private const int VisitReportPackageId = 240;
    private const int SnapshotRequestPackageId = 241;
    private const int SnapshotPackageId = 242;
    private const int TeleportRequestPackageId = 243;
    private const int TravelTransitionPackageId = 244;

    public void InitMod(Mod _modInstance)
    {
        VisitedTraderTeleportConfig.Configure(_modInstance);
        RegisterNetPackages();
        var harmony = new Harmony("anthr.7d2d.visitedtraderteleport");
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        Debug.Log("[VisitedTraderTeleport] Loaded.");
    }

    private static void RegisterNetPackages()
    {
        RegisterNetPackage(VisitReportPackageId, typeof(NetPackageVisitedTraderVisitReport));
        RegisterNetPackage(SnapshotRequestPackageId, typeof(NetPackageVisitedTraderSnapshotRequest));
        RegisterNetPackage(SnapshotPackageId, typeof(NetPackageVisitedTraderSnapshot));
        RegisterNetPackage(TeleportRequestPackageId, typeof(NetPackageVisitedTraderTeleportRequest));
        RegisterNetPackage(TravelTransitionPackageId, typeof(NetPackageVisitedTraderTravelTransition));
    }

    private static void RegisterNetPackage(int packageId, System.Type packageType)
    {
        try
        {
            System.Type[] mappings = NetPackageManager.PackageMappings;
            if (mappings == null || packageId < 0 || packageId >= mappings.Length)
            {
                Debug.LogWarning(
                    $"[VisitedTraderTeleport] Could not register {packageType.Name}: package id {packageId} is outside the available range.");
                return;
            }

            System.Type existingType = mappings[packageId];
            if (existingType == packageType)
            {
                return;
            }

            if (existingType != null)
            {
                Debug.LogWarning(
                    $"[VisitedTraderTeleport] Could not register {packageType.Name}: package id {packageId} is already used by {existingType.Name}.");
                return;
            }

            NetPackageManager.AddPackageMapping(packageId, packageType);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not register {packageType.Name}: {ex.Message}");
        }
    }
}
