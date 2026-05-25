using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VisitedTraderTeleportTestTools;

public sealed class ModApi : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        new Harmony("anthr.7d2d.visitedtraderteleport.testtools").PatchAll(Assembly.GetExecutingAssembly());
        Debug.Log("[VisitedTraderTeleportTestTools] Loaded.");
    }
}
