using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VisitedTraderTeleport;

public sealed class ModApi : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        new Harmony("anthr.7d2d.visitedtraderteleport").PatchAll(Assembly.GetExecutingAssembly());
        Debug.Log("[VisitedTraderTeleport] Loaded.");
    }
}
