using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VisitedTraderTeleport;

internal static class VisitedTraderStore
{
    private const string FileName = "VisitedTraderTeleportVisited.txt";
    private static readonly Dictionary<string, TraderDestination> Destinations = new();
    private static string loadedPath;

    public static IReadOnlyList<TraderDestination> GetDestinations()
    {
        EnsureLoaded();
        return Destinations.Values
            .OrderBy(destination => destination.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(destination => destination.AreaX)
            .ThenBy(destination => destination.AreaZ)
            .ToList();
    }

    public static bool TryGet(string key, out TraderDestination destination)
    {
        EnsureLoaded();
        return Destinations.TryGetValue(key, out destination);
    }

    public static string GetKey(EntityTrader trader)
    {
        if (trader == null)
        {
            return string.Empty;
        }

        Vector3 position = trader.position;
        int areaX = Mathf.RoundToInt(position.x);
        int areaZ = Mathf.RoundToInt(position.z);

        if (trader.traderArea != null)
        {
            areaX = trader.traderArea.Position.x;
            areaZ = trader.traderArea.Position.z;
        }

        string npcId = string.IsNullOrEmpty(trader.npcID) ? "trader" : trader.npcID;
        return $"{npcId}:{areaX}:{areaZ}";
    }

    public static void Record(EntityTrader trader, EntityPlayer player)
    {
        if (trader == null)
        {
            return;
        }

        EnsureLoaded();

        TraderDestination destination = CreateDestination(trader, player);
        if (destination == null)
        {
            return;
        }

        bool changed = !Destinations.TryGetValue(destination.Key, out TraderDestination existing) ||
                       existing.DisplayName != destination.DisplayName ||
                       Vector3.Distance(existing.Position, destination.Position) > 0.5f ||
                       Vector3.Distance(existing.Forward, destination.Forward) > 0.01f;

        if (!changed)
        {
            return;
        }

        Destinations[destination.Key] = destination;
        Save();
        Debug.Log($"[VisitedTraderTeleport] Recorded visited trader: {destination.DialogText}");
    }

    private static TraderDestination CreateDestination(EntityTrader trader, EntityPlayer player)
    {
        Vector3 position = player?.position ?? trader.position;
        int areaX = Mathf.RoundToInt(position.x);
        int areaZ = Mathf.RoundToInt(position.z);

        if (trader.traderArea != null)
        {
            areaX = trader.traderArea.Position.x;
            areaZ = trader.traderArea.Position.z;
        }

        string displayName = GetDisplayName(trader);

        return new TraderDestination
        {
            Key = GetKey(trader),
            DisplayName = displayName,
            Position = position,
            Forward = Vector3.zero,
            AreaX = areaX,
            AreaZ = areaZ
        };
    }

    private static string GetDisplayName(EntityTrader trader)
    {
        if (!string.IsNullOrWhiteSpace(trader.EntityName))
        {
            return trader.EntityName;
        }

        if (!string.IsNullOrWhiteSpace(trader.entityName))
        {
            return trader.entityName;
        }

        if (!string.IsNullOrWhiteSpace(trader.npcID))
        {
            return trader.npcID;
        }

        return "Trader";
    }

    private static void EnsureLoaded()
    {
        string path = GetStorePath();
        if (string.Equals(path, loadedPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Destinations.Clear();
        loadedPath = path;

        if (!File.Exists(path))
        {
            return;
        }

        foreach (string line in File.ReadAllLines(path))
        {
            if (TraderDestination.TryParse(line, out TraderDestination destination))
            {
                Destinations[destination.Key] = destination;
            }
        }
    }

    private static void Save()
    {
        string path = GetStorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        var lines = new List<string> { "# VisitedTraderTeleport visited trader destinations" };
        lines.AddRange(Destinations.Values
            .OrderBy(destination => destination.Key, StringComparer.OrdinalIgnoreCase)
            .Select(destination => destination.Serialize()));

        File.WriteAllLines(path, lines);
    }

    private static string GetStorePath()
    {
        try
        {
            string saveDir = GameIO.GetSaveGameDir();
            if (!string.IsNullOrEmpty(saveDir))
            {
                return Path.Combine(saveDir, FileName);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not resolve save directory: {ex.Message}");
        }

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods", "VisitedTraderTeleport", FileName);
    }
}
