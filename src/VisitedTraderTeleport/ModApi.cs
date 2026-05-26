using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VisitedTraderTeleport;

public sealed class ModApi : IModApi
{
    private const int PreferredVisitReportPackageId = 240;
    private const int PreferredSnapshotRequestPackageId = 241;
    private const int PreferredSnapshotPackageId = 242;
    private const int PreferredTeleportRequestPackageId = 243;
    private const int PreferredTravelTransitionPackageId = 244;

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
        RegisterNetPackage(PreferredVisitReportPackageId, typeof(NetPackageVisitedTraderVisitReport));
        RegisterNetPackage(PreferredSnapshotRequestPackageId, typeof(NetPackageVisitedTraderSnapshotRequest));
        RegisterNetPackage(PreferredSnapshotPackageId, typeof(NetPackageVisitedTraderSnapshot));
        RegisterNetPackage(PreferredTeleportRequestPackageId, typeof(NetPackageVisitedTraderTeleportRequest));
        RegisterNetPackage(PreferredTravelTransitionPackageId, typeof(NetPackageVisitedTraderTravelTransition));
    }

    private static void RegisterNetPackage(int preferredPackageId, System.Type packageType)
    {
        try
        {
            System.Type[] mappings = NetPackageManager.PackageMappings;
            if (mappings == null || mappings.Length == 0)
            {
                Debug.LogWarning(
                    $"[VisitedTraderTeleport] Could not register {packageType.Name}: package mappings are not available.");
                return;
            }

            int existingPackageId = GetRegisteredPackageId(packageType);
            if (existingPackageId >= 0 && existingPackageId < mappings.Length && mappings[existingPackageId] == packageType)
            {
                return;
            }

            int packageId = ResolvePackageId(mappings, preferredPackageId);
            if (packageId < 0)
            {
                Debug.LogWarning(
                    $"[VisitedTraderTeleport] Could not register {packageType.Name}: no free package id was found.");
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
            Debug.Log($"[VisitedTraderTeleport] Registered {packageType.Name} as net package id {packageId}.");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not register {packageType.Name}: {ex.Message}");
        }
    }

    private static int ResolvePackageId(System.Type[] mappings, int preferredPackageId)
    {
        if (preferredPackageId >= 0 &&
            preferredPackageId < mappings.Length &&
            mappings[preferredPackageId] == null)
        {
            return preferredPackageId;
        }

        for (int i = mappings.Length - 1; i >= 0; i--)
        {
            if (mappings[i] == null)
            {
                return i;
            }
        }

        return -1;
    }

    private static int GetRegisteredPackageId(System.Type packageType)
    {
        try
        {
            return NetPackageManager.GetPackageId(packageType);
        }
        catch
        {
            return -1;
        }
    }
}
