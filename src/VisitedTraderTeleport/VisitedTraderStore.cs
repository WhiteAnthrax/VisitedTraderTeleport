using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace VisitedTraderTeleport;

internal static class VisitedTraderStore
{
    private const string LegacyFileName = "VisitedTraderTeleportVisited.txt";
    private const string DatabaseFileName = "VisitedTraderTeleportData.json";

    private static readonly Dictionary<string, TraderDestination> LegacyDestinations = new();
    private static VisitedTraderDatabase database = new();
    private static string loadedSaveDirectory;

    public static IReadOnlyList<TraderDestination> GetDestinations(EntityPlayer player)
    {
        if (VisitedTraderNetwork.IsClientOnly)
        {
            return VisitedTraderClientState.GetDestinations();
        }

        EnsureLoaded();

        var keys = new HashSet<string>(LegacyDestinations.Keys, StringComparer.Ordinal);
        foreach (string key in GetAllowedNewSchemaKeys(player))
        {
            keys.Add(key);
        }

        return keys
            .Select(TryResolveDestination)
            .Where(destination => destination != null)
            .OrderBy(destination => destination.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(destination => destination.AreaX)
            .ThenBy(destination => destination.AreaZ)
            .ToList();
    }

    public static bool TryGet(string key, EntityPlayer player, out TraderDestination destination)
    {
        if (VisitedTraderNetwork.IsClientOnly)
        {
            return VisitedTraderClientState.TryGet(key, out destination);
        }

        EnsureLoaded();
        destination = null;

        if (LegacyDestinations.TryGetValue(key, out TraderDestination legacyDestination))
        {
            destination = legacyDestination;
            return true;
        }

        HashSet<string> allowedKeys = GetAllowedNewSchemaKeys(player);
        if (!allowedKeys.Contains(key))
        {
            return false;
        }

        destination = TryResolveDestination(key);
        return destination != null;
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
        if (trader == null || player == null)
        {
            return;
        }

        if (VisitedTraderNetwork.IsClientOnly)
        {
            VisitedTraderNetwork.ReportVisit(CreateVisitReport(trader));
            return;
        }

        EnsureLoaded();

        string playerKey = GetPlayerKey(player);
        if (string.IsNullOrEmpty(playerKey))
        {
            Debug.LogWarning("[VisitedTraderTeleport] Could not resolve player key; visit was not recorded.");
            return;
        }

        TraderDestination destination = CreateDestination(trader, player);
        bool changed = UpsertTrader(destination);

        if (!database.VisitsByPlayer.TryGetValue(playerKey, out HashSet<string> playerVisits))
        {
            playerVisits = new HashSet<string>(StringComparer.Ordinal);
            database.VisitsByPlayer[playerKey] = playerVisits;
        }

        if (playerVisits.Add(destination.Key))
        {
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        SaveDatabase();
        Debug.Log($"[VisitedTraderTeleport] Recorded visited trader for {player.PlayerDisplayName}: {destination.DialogText}");
    }

    public static void RecordReportedVisit(TraderVisitReport report, EntityPlayer player)
    {
        if (report == null || player == null || string.IsNullOrEmpty(report.Key))
        {
            return;
        }

        EnsureLoaded();

        string playerKey = GetPlayerKey(player);
        if (string.IsNullOrEmpty(playerKey))
        {
            Debug.LogWarning("[VisitedTraderTeleport] Could not resolve player key for reported visit.");
            return;
        }

        TraderDestination destination = CreateDestination(report, player);
        bool changed = UpsertTrader(destination);

        if (!database.VisitsByPlayer.TryGetValue(playerKey, out HashSet<string> playerVisits))
        {
            playerVisits = new HashSet<string>(StringComparer.Ordinal);
            database.VisitsByPlayer[playerKey] = playerVisits;
        }

        if (playerVisits.Add(destination.Key))
        {
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        SaveDatabase();
        Debug.Log($"[VisitedTraderTeleport] Recorded reported visited trader for {player.PlayerDisplayName}: {destination.DialogText}");
    }

    private static HashSet<string> GetAllowedNewSchemaKeys(EntityPlayer player)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        AccessMode mode = VisitedTraderTeleportConfig.AccessMode;

        if (mode == AccessMode.Shared)
        {
            foreach (HashSet<string> visits in database.VisitsByPlayer.Values)
            {
                keys.UnionWith(visits);
            }

            return keys;
        }

        IEnumerable<string> playerKeys = mode == AccessMode.Party
            ? GetPartyPlayerKeys(player)
            : new[] { GetPlayerKey(player) };

        foreach (string playerKey in playerKeys.Where(key => !string.IsNullOrEmpty(key)))
        {
            if (database.VisitsByPlayer.TryGetValue(playerKey, out HashSet<string> visits))
            {
                keys.UnionWith(visits);
            }
        }

        return keys;
    }

    private static IEnumerable<string> GetPartyPlayerKeys(EntityPlayer player)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        string ownKey = GetPlayerKey(player);
        if (!string.IsNullOrEmpty(ownKey))
        {
            keys.Add(ownKey);
        }

        Party party = player?.Party;
        if (party?.MemberList == null)
        {
            return keys;
        }

        foreach (EntityPlayer member in party.MemberList)
        {
            string memberKey = GetPlayerKey(member);
            if (!string.IsNullOrEmpty(memberKey))
            {
                keys.Add(memberKey);
            }
        }

        return keys;
    }

    private static string GetPlayerKey(EntityPlayer player)
    {
        if (player == null)
        {
            return string.Empty;
        }

        if (player is EntityPlayerLocal localPlayer &&
            localPlayer.persistentPlayerData?.PrimaryId != null)
        {
            return localPlayer.persistentPlayerData.PrimaryId.ToString();
        }

        ClientInfo clientInfo = ConnectionManager.Instance?.Clients?.ForEntityId(player.entityId);
        if (clientInfo?.InternalId != null)
        {
            return clientInfo.InternalId.ToString();
        }

        return player.belongsPlayerId > 0
            ? $"belongs:{player.belongsPlayerId}"
            : $"entity:{player.entityId}";
    }

    private static TraderDestination CreateDestination(EntityTrader trader, EntityPlayer player)
    {
        Vector3 position = player.position;
        int areaX = Mathf.RoundToInt(position.x);
        int areaZ = Mathf.RoundToInt(position.z);

        if (trader.traderArea != null)
        {
            areaX = trader.traderArea.Position.x;
            areaZ = trader.traderArea.Position.z;
        }

        return new TraderDestination
        {
            Key = GetKey(trader),
            DisplayName = GetDisplayName(trader),
            Position = position,
            Forward = Vector3.zero,
            AreaX = areaX,
            AreaZ = areaZ
        };
    }

    private static TraderDestination CreateDestination(TraderVisitReport report, EntityPlayer player)
    {
        return new TraderDestination
        {
            Key = report.Key,
            DisplayName = string.IsNullOrWhiteSpace(report.DisplayName) ? "Trader" : report.DisplayName,
            Position = player.position,
            Forward = Vector3.zero,
            AreaX = report.AreaX,
            AreaZ = report.AreaZ
        };
    }

    private static TraderVisitReport CreateVisitReport(EntityTrader trader)
    {
        int areaX = Mathf.RoundToInt(trader.position.x);
        int areaZ = Mathf.RoundToInt(trader.position.z);

        if (trader.traderArea != null)
        {
            areaX = trader.traderArea.Position.x;
            areaZ = trader.traderArea.Position.z;
        }

        return new TraderVisitReport
        {
            Key = GetKey(trader),
            DisplayName = GetDisplayName(trader),
            AreaX = areaX,
            AreaZ = areaZ
        };
    }

    private static bool UpsertTrader(TraderDestination destination)
    {
        if (!database.Traders.TryGetValue(destination.Key, out TraderDestinationRecord existing))
        {
            database.Traders[destination.Key] = ToRecord(destination);
            return true;
        }

        bool changed =
            existing.DisplayName != destination.DisplayName ||
            existing.PositionX != destination.Position.x ||
            existing.PositionY != destination.Position.y ||
            existing.PositionZ != destination.Position.z ||
            existing.ForwardX != destination.Forward.x ||
            existing.ForwardY != destination.Forward.y ||
            existing.ForwardZ != destination.Forward.z ||
            existing.AreaX != destination.AreaX ||
            existing.AreaZ != destination.AreaZ;

        if (changed)
        {
            database.Traders[destination.Key] = ToRecord(destination);
        }

        return changed;
    }

    private static TraderDestination TryResolveDestination(string key)
    {
        if (LegacyDestinations.TryGetValue(key, out TraderDestination legacyDestination))
        {
            return legacyDestination;
        }

        if (!database.Traders.TryGetValue(key, out TraderDestinationRecord record))
        {
            return null;
        }

        return new TraderDestination
        {
            Key = record.Key,
            DisplayName = record.DisplayName,
            Position = new Vector3(record.PositionX, record.PositionY, record.PositionZ),
            Forward = new Vector3(record.ForwardX, record.ForwardY, record.ForwardZ),
            AreaX = record.AreaX,
            AreaZ = record.AreaZ
        };
    }

    private static TraderDestinationRecord ToRecord(TraderDestination destination)
    {
        return new TraderDestinationRecord
        {
            Key = destination.Key,
            DisplayName = destination.DisplayName,
            PositionX = destination.Position.x,
            PositionY = destination.Position.y,
            PositionZ = destination.Position.z,
            ForwardX = destination.Forward.x,
            ForwardY = destination.Forward.y,
            ForwardZ = destination.Forward.z,
            AreaX = destination.AreaX,
            AreaZ = destination.AreaZ
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
        string saveDirectory = GetSaveDirectory();
        if (string.Equals(saveDirectory, loadedSaveDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        loadedSaveDirectory = saveDirectory;
        LegacyDestinations.Clear();
        database = new VisitedTraderDatabase();

        LoadLegacyDestinations();
        LoadDatabase();
    }

    private static void LoadLegacyDestinations()
    {
        string path = Path.Combine(loadedSaveDirectory, LegacyFileName);
        if (!File.Exists(path))
        {
            return;
        }

        foreach (string line in File.ReadAllLines(path))
        {
            if (TraderDestination.TryParse(line, out TraderDestination destination))
            {
                LegacyDestinations[destination.Key] = destination;
            }
        }
    }

    private static void LoadDatabase()
    {
        string path = Path.Combine(loadedSaveDirectory, DatabaseFileName);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            VisitedTraderDatabase loaded = JsonConvert.DeserializeObject<VisitedTraderDatabase>(File.ReadAllText(path));
            database = loaded ?? new VisitedTraderDatabase();
            database.Traders ??= new Dictionary<string, TraderDestinationRecord>();
            database.VisitsByPlayer ??= new Dictionary<string, HashSet<string>>();
        }
        catch (Exception ex)
        {
            database = new VisitedTraderDatabase();
            Debug.LogWarning($"[VisitedTraderTeleport] Could not read JSON database: {ex.Message}");
        }
    }

    private static void SaveDatabase()
    {
        Directory.CreateDirectory(loadedSaveDirectory);
        string path = Path.Combine(loadedSaveDirectory, DatabaseFileName);
        File.WriteAllText(path, JsonConvert.SerializeObject(database, Formatting.Indented));
    }

    private static string GetSaveDirectory()
    {
        try
        {
            string saveDir = GameIO.GetSaveGameDir();
            if (!string.IsNullOrEmpty(saveDir))
            {
                return saveDir;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not resolve save directory: {ex.Message}");
        }

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods", "VisitedTraderTeleport");
    }
}
