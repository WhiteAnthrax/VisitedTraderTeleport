using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TraderTeleport;

public sealed class ModApi : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        new Harmony("anthr.7d2d.traderteleport").PatchAll(Assembly.GetExecutingAssembly());
        Debug.Log("[TraderTeleport] Loaded.");
    }
}
