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
    private const int PreferredForgetRequestPackageId = 245;
    private static bool isRegisteringNetPackages;

    public void InitMod(Mod _modInstance)
    {
        VisitedTraderTeleportConfig.Configure(_modInstance);
        var harmony = new Harmony("anthr.7d2d.visitedtraderteleport");
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        RegisterNetPackages("mod init");
        Debug.Log("[VisitedTraderTeleport] Loaded.");
    }

    internal static void RegisterNetPackages(string reason)
    {
        if (isRegisteringNetPackages)
        {
            return;
        }

        isRegisteringNetPackages = true;
        try
        {
            RegisterNetPackage(PreferredVisitReportPackageId, typeof(NetPackageVisitedTraderVisitReport), reason);
            RegisterNetPackage(PreferredSnapshotRequestPackageId, typeof(NetPackageVisitedTraderSnapshotRequest), reason);
            RegisterNetPackage(PreferredSnapshotPackageId, typeof(NetPackageVisitedTraderSnapshot), reason);
            RegisterNetPackage(PreferredTeleportRequestPackageId, typeof(NetPackageVisitedTraderTeleportRequest), reason);
            RegisterNetPackage(PreferredTravelTransitionPackageId, typeof(NetPackageVisitedTraderTravelTransition), reason);
            RegisterNetPackage(PreferredForgetRequestPackageId, typeof(NetPackageVisitedTraderForgetRequest), reason);
        }
        finally
        {
            isRegisteringNetPackages = false;
        }
    }

    private static void RegisterNetPackage(int preferredPackageId, System.Type packageType, string reason)
    {
        try
        {
            System.Type[] mappings = NetPackageManager.PackageMappings;
            if (mappings == null || mappings.Length == 0)
            {
                Debug.LogWarning(
                    $"[VisitedTraderTeleport] Could not register {packageType.Name} during {reason}: package mappings are not available.");
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
                    $"[VisitedTraderTeleport] Could not register {packageType.Name} during {reason}: no free package id was found.");
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
                    $"[VisitedTraderTeleport] Could not register {packageType.Name} during {reason}: package id {packageId} is already used by {existingType.Name}.");
                return;
            }

            NetPackageManager.AddPackageMapping(packageId, packageType);
            Debug.Log($"[VisitedTraderTeleport] Registered {packageType.Name} as net package id {packageId} during {reason}.");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not register {packageType.Name} during {reason}: {ex.Message}");
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

[HarmonyPatch(typeof(NetPackageManager), "SetupBaseMapping")]
internal static class NetPackageManagerSetupBaseMappingPatch
{
    public static void Postfix()
    {
        ModApi.RegisterNetPackages("net package base mapping setup");
    }
}
