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
    private const string DatabaseNormalizationBackupFileName = "VisitedTraderTeleportData.before-0.4.16.json";
    private const float TestTraderAreaPadding = 8f;
    private const float SameTraderPositionTolerance = 16f;
    private const float SameDetailedTraderPositionTolerance = 6f;
    private const int TraderPositionKeyBucketSize = 4;

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

        return DeduplicateDestinations(keys
            .Select(TryResolveDestination)
            .Where(destination => destination != null)
            .OrderBy(destination => destination.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(destination => destination.AreaX)
            .ThenBy(destination => destination.AreaZ));
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

        return GetKey(trader, trader.traderArea);
    }

    private static string GetKey(EntityTrader trader, TraderArea traderArea)
    {
        if (trader == null)
        {
            return string.Empty;
        }

        Vector3 position = trader.position;
        int areaX = traderArea?.Position.x ?? Mathf.RoundToInt(position.x);
        int areaZ = traderArea?.Position.z ?? Mathf.RoundToInt(position.z);
        string npcId = string.IsNullOrEmpty(trader.npcID) ? "trader" : trader.npcID;
        return $"{npcId}:{areaX}:{areaZ}";
    }

    public static TraderDestination CreateCurrentTraderDestination(EntityTrader trader)
    {
        if (trader == null)
        {
            return null;
        }

        Vector3 position = trader.position;
        int areaX = Mathf.RoundToInt(position.x);
        int areaZ = Mathf.RoundToInt(position.z);

        if (trader.traderArea != null)
        {
            areaX = trader.traderArea.Position.x;
            areaZ = trader.traderArea.Position.z;
        }

        return CanonicalizeDestination(new TraderDestination
        {
            Key = GetKey(trader),
            DisplayName = GetDisplayName(trader),
            Position = position,
            Forward = Vector3.zero,
            AreaX = areaX,
            AreaZ = areaZ,
            Biome = ResolveBiomeName(position)
        }, position);
    }

    // The biome can only be read while the chunk is loaded, which is the case while the
    // player is at the trader. Capture it here so it survives to the (far, unloaded) list.
    private static string ResolveBiomeName(Vector3 position)
    {
        try
        {
            BiomeDefinition biome = GameManager.Instance?.World?.GetBiome(
                Mathf.FloorToInt(position.x),
                Mathf.FloorToInt(position.z));
            return biome?.m_sBiomeName ?? string.Empty;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not resolve biome at record time: {ex.Message}");
            return string.Empty;
        }
    }

    public static bool IsSameTrader(TraderDestination destination, TraderDestination currentTrader)
    {
        if (destination == null || currentTrader == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(destination.Key) &&
            string.Equals(destination.Key, currentTrader.Key, StringComparison.Ordinal))
        {
            return true;
        }

        return IsSameTraderByNearbyPosition(destination, currentTrader);
    }

    private static bool IsSameTraderByNearbyPosition(TraderDestination destination, TraderDestination currentTrader)
    {
        if (!HasCompatibleTraderIdentity(destination, currentTrader))
        {
            return false;
        }

        if (IsSameNamedTraderInSameArea(destination, currentTrader))
        {
            return true;
        }

        Vector3 delta = destination.Position - currentTrader.Position;
        delta.y = 0f;
        float tolerance = HasLocalPositionInKey(destination.Key) && HasLocalPositionInKey(currentTrader.Key)
            ? SameDetailedTraderPositionTolerance
            : SameTraderPositionTolerance;
        return delta.sqrMagnitude <= tolerance * tolerance;
    }

    private static bool HasCompatibleTraderIdentity(TraderDestination left, TraderDestination right)
    {
        string leftPrefix = NormalizeTraderIdentityToken(GetKeyPrefix(left.Key));
        string rightPrefix = NormalizeTraderIdentityToken(GetKeyPrefix(right.Key));
        if (!string.IsNullOrEmpty(leftPrefix) &&
            string.Equals(leftPrefix, rightPrefix, StringComparison.Ordinal))
        {
            return true;
        }

        string leftName = NormalizeTraderIdentityToken(left.DisplayName);
        string rightName = NormalizeTraderIdentityToken(right.DisplayName);
        if (!string.IsNullOrEmpty(leftName) &&
            string.Equals(leftName, rightName, StringComparison.Ordinal))
        {
            return true;
        }

        return
            (!string.IsNullOrEmpty(leftPrefix) &&
             string.Equals(leftPrefix, rightName, StringComparison.Ordinal)) ||
            (!string.IsNullOrEmpty(rightPrefix) &&
             string.Equals(rightPrefix, leftName, StringComparison.Ordinal));
    }

    private static bool IsSameNamedTraderInSameArea(TraderDestination left, TraderDestination right)
    {
        if (left.AreaX != right.AreaX || left.AreaZ != right.AreaZ)
        {
            return false;
        }

        string leftName = NormalizeTraderIdentityToken(left.DisplayName);
        string rightName = NormalizeTraderIdentityToken(right.DisplayName);
        return !string.IsNullOrEmpty(leftName) &&
               string.Equals(leftName, rightName, StringComparison.Ordinal);
    }

    private static string NormalizeTraderIdentityToken(string value)
    {
        string token = NormalizeDisplayNameToken(value);
        if (token.StartsWith("npc", StringComparison.Ordinal) && token.Length > 3)
        {
            token = token.Substring(3);
        }

        if (token.StartsWith("traitor", StringComparison.Ordinal) && token.Length > 7)
        {
            token = "trader" + token.Substring(7);
        }

        return token;
    }

    private static string NormalizeDisplayNameToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    private static List<TraderDestination> DeduplicateDestinations(IEnumerable<TraderDestination> destinations)
    {
        var results = new List<TraderDestination>();
        foreach (TraderDestination destination in destinations)
        {
            int existingIndex = results.FindIndex(existing => IsSameTrader(existing, destination));
            if (existingIndex < 0)
            {
                results.Add(destination);
                continue;
            }

            if (IsMoreSpecificKey(destination.Key, results[existingIndex].Key))
            {
                results[existingIndex] = destination;
            }
        }

        return results;
    }

    private static bool IsMoreSpecificKey(string candidate, string existing)
    {
        return GetKeyPartCount(candidate) > GetKeyPartCount(existing);
    }

    private static bool HasLocalPositionInKey(string key)
    {
        return GetKeyPartCount(key) >= 5;
    }

    private static int GetKeyPartCount(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return 0;
        }

        return key.Count(c => c == ':') + 1;
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

        TraderDestination destination = ReuseExistingTraderKey(
            CanonicalizeDestination(CreateDestination(trader, player), trader.position));
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
            Debug.Log($"[VisitedTraderTeleport] Visit already recorded for {player.PlayerDisplayName}: {destination.DialogText}");
            return;
        }

        SaveDatabase();
        Debug.Log($"[VisitedTraderTeleport] Recorded visited trader for {player.PlayerDisplayName}: {destination.DialogText}");
    }

    public static void RecordReportedVisit(TraderVisitReport report, EntityPlayer player)
    {
        if (player == null)
        {
            Debug.LogWarning("[VisitedTraderTeleport] Ignored reported visit because player could not be resolved.");
            return;
        }

        if (report == null || string.IsNullOrEmpty(report.Key))
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Ignored reported visit for {player.PlayerDisplayName} because the trader key was empty.");
            return;
        }

        EnsureLoaded();

        string playerKey = GetPlayerKey(player);
        if (string.IsNullOrEmpty(playerKey))
        {
            Debug.LogWarning("[VisitedTraderTeleport] Could not resolve player key for reported visit.");
            return;
        }

        TraderDestination destination = ReuseExistingTraderKey(
            CanonicalizeDestination(
                CreateDestination(report, player),
                report.HasTraderPosition
                    ? new Vector3(report.TraderPositionX, report.TraderPositionY, report.TraderPositionZ)
                    : (Vector3?)null));
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
            Debug.Log($"[VisitedTraderTeleport] Reported visit already recorded for {player.PlayerDisplayName}: {destination.DialogText}");
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
            AreaZ = areaZ,
            Biome = ResolveBiomeName(trader.position)
        };
    }

    private static TraderDestination CreateDestination(TraderVisitReport report, EntityPlayer player)
    {
        // A client reports a visit while standing at the trader, so the trader chunk is loaded
        // on the server here; resolve the biome from the reported trader position.
        Vector3 biomePosition = report.HasTraderPosition
            ? new Vector3(report.TraderPositionX, report.TraderPositionY, report.TraderPositionZ)
            : player.position;

        return new TraderDestination
        {
            Key = report.Key,
            DisplayName = string.IsNullOrWhiteSpace(report.DisplayName) ? "Trader" : report.DisplayName,
            Position = player.position,
            Forward = Vector3.zero,
            AreaX = report.AreaX,
            AreaZ = report.AreaZ,
            Biome = ResolveBiomeName(biomePosition)
        };
    }

    private static TraderDestination CanonicalizeDestination(TraderDestination destination, Vector3? identityPosition = null)
    {
        if (destination == null)
        {
            return null;
        }

        Vector3 keyPosition = identityPosition ?? destination.Position;
        TraderArea traderArea = FindTraderAreaForPosition(keyPosition) ?? FindTraderAreaForPosition(destination.Position);
        if (traderArea == null)
        {
            return destination;
        }

        string keyPrefix = GetKeyPrefix(destination.Key);
        if (string.IsNullOrEmpty(keyPrefix))
        {
            keyPrefix = "trader";
        }

        string canonicalKey = BuildCanonicalKey(keyPrefix, traderArea, keyPosition);
        if (string.Equals(destination.Key, canonicalKey, StringComparison.Ordinal) &&
            destination.AreaX == traderArea.Position.x &&
            destination.AreaZ == traderArea.Position.z)
        {
            return destination;
        }

        return new TraderDestination
        {
            Key = canonicalKey,
            DisplayName = destination.DisplayName,
            Position = destination.Position,
            Forward = destination.Forward,
            AreaX = traderArea.Position.x,
            AreaZ = traderArea.Position.z,
            Biome = destination.Biome
        };
    }

    private static string GetKeyPrefix(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        int separator = key.IndexOf(':');
        return (separator > 0 ? key.Substring(0, separator) : key).Trim().ToLowerInvariant();
    }

    private static string BuildCanonicalKey(string keyPrefix, TraderArea traderArea, Vector3 position)
    {
        int localX = QuantizeTraderLocalPosition(position.x - traderArea.Position.x);
        int localZ = QuantizeTraderLocalPosition(position.z - traderArea.Position.z);
        return $"{keyPrefix}:{traderArea.Position.x}:{traderArea.Position.z}:{localX}:{localZ}";
    }

    private static int QuantizeTraderLocalPosition(float value)
    {
        return Mathf.RoundToInt(value / TraderPositionKeyBucketSize) * TraderPositionKeyBucketSize;
    }

    private static TraderArea FindTraderAreaForPosition(Vector3 position)
    {
        World world = GameManager.Instance?.World;
        IEnumerable<TraderArea> traderAreas = world?.TraderAreas;
        if (traderAreas == null)
        {
            return null;
        }

        TraderArea bestArea = null;
        float bestDistanceSq = float.MaxValue;
        foreach (TraderArea traderArea in traderAreas)
        {
            if (traderArea == null)
            {
                continue;
            }

            Bounds bounds = GetTraderAreaBounds(traderArea);
            if (!bounds.Contains(position))
            {
                continue;
            }

            Vector3 delta = bounds.center - position;
            delta.y = 0f;
            float distanceSq = delta.sqrMagnitude;
            if (distanceSq < bestDistanceSq)
            {
                bestArea = traderArea;
                bestDistanceSq = distanceSq;
            }
        }

        return bestArea;
    }

    private static Bounds GetTraderAreaBounds(TraderArea traderArea)
    {
        Vector3 position = traderArea.Position;
        Vector3 size = traderArea.PrefabSize;
        if (size.x < 1f)
        {
            size.x = 1f;
        }

        if (size.y < 1f)
        {
            size.y = 1f;
        }

        if (size.z < 1f)
        {
            size.z = 1f;
        }

        Vector3 center = position + size * 0.5f;
        size.x += TestTraderAreaPadding * 2f;
        size.y += 64f;
        size.z += TestTraderAreaPadding * 2f;
        return new Bounds(center, size);
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
            AreaZ = areaZ,
            TraderPositionX = trader.position.x,
            TraderPositionY = trader.position.y,
            TraderPositionZ = trader.position.z
        };
    }

    private static bool UpsertTrader(TraderDestination destination)
    {
        if (!database.Traders.TryGetValue(destination.Key, out TraderDestinationRecord existing))
        {
            database.Traders[destination.Key] = ToRecord(destination);
            return true;
        }

        // Keep a previously captured biome if this visit could not resolve one.
        if (string.IsNullOrEmpty(destination.Biome) && !string.IsNullOrEmpty(existing.Biome))
        {
            destination.Biome = existing.Biome;
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
            existing.AreaZ != destination.AreaZ ||
            (existing.Biome ?? string.Empty) != (destination.Biome ?? string.Empty);

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
            AreaZ = record.AreaZ,
            Biome = record.Biome
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
            AreaZ = destination.AreaZ,
            Biome = destination.Biome
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
        NormalizeDatabase();
    }

    private static void LoadLegacyDestinations()
    {
        string path = Path.Combine(loadedSaveDirectory, LegacyFileName);
        if (!File.Exists(path))
        {
            return;
        }

        int loadedCount = 0;
        int normalizedCount = 0;
        foreach (string line in File.ReadAllLines(path))
        {
            if (TraderDestination.TryParse(line, out TraderDestination destination))
            {
                string originalKey = destination.Key;
                destination = CanonicalizeDestination(destination);
                LegacyDestinations[destination.Key] = destination;
                loadedCount++;
                if (!string.Equals(originalKey, destination.Key, StringComparison.Ordinal))
                {
                    normalizedCount++;
                }
            }
        }

        Debug.Log(
            $"[VisitedTraderTeleport] Loaded legacy TXT destinations: " +
            $"{loadedCount} entries, normalized={normalizedCount}.");
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

    private static void NormalizeDatabase()
    {
        if (database.Traders == null || database.Traders.Count == 0)
        {
            return;
        }

        var keyAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var normalizedTraders = new Dictionary<string, TraderDestinationRecord>(StringComparer.Ordinal);
        int normalizedTraderKeys = 0;
        int mergedTraderRecords = 0;
        bool normalizedTraderRecords = false;

        foreach (KeyValuePair<string, TraderDestinationRecord> pair in database.Traders)
        {
            TraderDestination destination = FromRecord(pair.Value, pair.Key);
            if (destination == null || string.IsNullOrEmpty(destination.Key))
            {
                continue;
            }

            TraderDestination canonical = CanonicalizeDestination(destination);
            if (canonical == null || string.IsNullOrEmpty(canonical.Key))
            {
                continue;
            }

            string targetKey = canonical.Key;
            if (TryFindSameNormalizedTraderKey(normalizedTraders, canonical, out string existingKey))
            {
                targetKey = existingKey;
                if (!string.Equals(canonical.Key, targetKey, StringComparison.Ordinal))
                {
                    keyAliases[canonical.Key] = targetKey;
                    canonical = WithKey(canonical, targetKey);
                    mergedTraderRecords++;
                }
            }

            keyAliases[pair.Key] = targetKey;
            keyAliases[destination.Key] = targetKey;

            bool keyChanged =
                !string.Equals(pair.Key, targetKey, StringComparison.Ordinal) ||
                !string.Equals(destination.Key, targetKey, StringComparison.Ordinal);
            if (keyChanged)
            {
                normalizedTraderKeys++;
            }

            TraderDestinationRecord canonicalRecord = ToRecord(canonical);
            bool hasExisting = normalizedTraders.TryGetValue(targetKey, out TraderDestinationRecord existing);
            bool recordChangedFromOriginal = !RecordsEqual(pair.Value, canonicalRecord);
            if (!hasExisting || keyChanged || !RecordsEqual(existing, canonicalRecord))
            {
                normalizedTraders[targetKey] = canonicalRecord;
            }

            normalizedTraderRecords |= recordChangedFromOriginal;
        }

        bool changed =
            normalizedTraderKeys > 0 ||
            mergedTraderRecords > 0 ||
            normalizedTraderRecords ||
            normalizedTraders.Count != database.Traders.Count;
        int normalizedVisits = 0;
        var normalizedVisitsByPlayer = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, HashSet<string>> pair in database.VisitsByPlayer)
        {
            var visits = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in pair.Value ?? Enumerable.Empty<string>())
            {
                string normalizedKey = keyAliases.TryGetValue(key, out string alias) ? alias : key;
                if (!string.Equals(key, normalizedKey, StringComparison.Ordinal))
                {
                    normalizedVisits++;
                }

                visits.Add(normalizedKey);
            }

            if (visits.Count != (pair.Value?.Count ?? 0))
            {
                changed = true;
            }

            normalizedVisitsByPlayer[pair.Key] = visits;
        }

        if (normalizedVisits > 0)
        {
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        database.Traders = normalizedTraders;
        database.VisitsByPlayer = normalizedVisitsByPlayer;
        BackupDatabaseBeforeNormalization();
        SaveDatabase();
        Debug.Log(
            $"[VisitedTraderTeleport] Normalized visited trader data: " +
            $"traderKeys={normalizedTraderKeys}, merged={mergedTraderRecords}, " +
            $"visitKeys={normalizedVisits}, traders={database.Traders.Count}.");
    }

    private static bool TryFindSameNormalizedTraderKey(
        Dictionary<string, TraderDestinationRecord> normalizedTraders,
        TraderDestination destination,
        out string key)
    {
        foreach (KeyValuePair<string, TraderDestinationRecord> pair in normalizedTraders)
        {
            TraderDestination existing = FromRecord(pair.Value, pair.Key);
            if (IsSameTrader(existing, destination))
            {
                key = pair.Key;
                return true;
            }
        }

        key = string.Empty;
        return false;
    }

    private static TraderDestination ReuseExistingTraderKey(TraderDestination destination)
    {
        if (destination == null || database.Traders == null || database.Traders.Count == 0)
        {
            return destination;
        }

        if (TryFindSameNormalizedTraderKey(database.Traders, destination, out string existingKey) &&
            !string.Equals(existingKey, destination.Key, StringComparison.Ordinal))
        {
            return WithKey(destination, existingKey);
        }

        return destination;
    }

    private static TraderDestination WithKey(TraderDestination destination, string key)
    {
        return new TraderDestination
        {
            Key = key,
            DisplayName = destination.DisplayName,
            Position = destination.Position,
            Forward = destination.Forward,
            AreaX = destination.AreaX,
            AreaZ = destination.AreaZ,
            Biome = destination.Biome
        };
    }

    private static void BackupDatabaseBeforeNormalization()
    {
        string path = Path.Combine(loadedSaveDirectory, DatabaseFileName);
        string backupPath = Path.Combine(loadedSaveDirectory, DatabaseNormalizationBackupFileName);
        if (!File.Exists(path) || File.Exists(backupPath))
        {
            return;
        }

        try
        {
            File.Copy(path, backupPath, false);
            Debug.Log($"[VisitedTraderTeleport] Created visited trader data backup: {DatabaseNormalizationBackupFileName}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not create visited trader data backup: {ex.Message}");
        }
    }

    private static TraderDestination FromRecord(TraderDestinationRecord record, string fallbackKey)
    {
        if (record == null)
        {
            return null;
        }

        return new TraderDestination
        {
            Key = string.IsNullOrEmpty(record.Key) ? fallbackKey : record.Key,
            DisplayName = record.DisplayName,
            Position = new Vector3(record.PositionX, record.PositionY, record.PositionZ),
            Forward = new Vector3(record.ForwardX, record.ForwardY, record.ForwardZ),
            AreaX = record.AreaX,
            AreaZ = record.AreaZ,
            Biome = record.Biome
        };
    }

    private static bool RecordsEqual(TraderDestinationRecord left, TraderDestinationRecord right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        return left.Key == right.Key &&
               left.DisplayName == right.DisplayName &&
               left.PositionX == right.PositionX &&
               left.PositionY == right.PositionY &&
               left.PositionZ == right.PositionZ &&
               left.ForwardX == right.ForwardX &&
               left.ForwardY == right.ForwardY &&
               left.ForwardZ == right.ForwardZ &&
               left.AreaX == right.AreaX &&
               left.AreaZ == right.AreaZ &&
               (left.Biome ?? string.Empty) == (right.Biome ?? string.Empty);
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
