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

    private static readonly Dictionary<string, TraderDestination> LegacyDestinations = new();
    private static readonly ITraderAreaLookup traderAreaLookup = new GameTraderAreaLookup();
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

        return TraderMatching.DeduplicateDestinations(keys
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
            Position = position.ToPosition3(),
            Forward = Position3.Zero,
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
        return TraderMatching.IsSameTrader(destination, currentTrader);
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

    // Removes this player's visit to one trader. Server-side (or single player); a connected
    // client asks the server to do it - see VisitedTraderNetwork.RequestForget - because the
    // client only ever holds a snapshot of what it is allowed to see.
    //
    // Only ever touches the calling player's own record. In Party and Shared mode the list is
    // the union of several players' visits, so the entry can survive this; the outcome says
    // which happened, and the dialog tells the player rather than looking like it did nothing.
    public static ForgetOutcome Forget(string destinationKey, EntityPlayer player)
    {
        EnsureLoaded();

        string playerKey = GetPlayerKey(player);
        if (string.IsNullOrEmpty(playerKey))
        {
            Debug.LogWarning("[VisitedTraderTeleport] Could not resolve player key; nothing was forgotten.");
            return ForgetOutcome.NotOnTheirList;
        }

        // Whether the destination is on this player's list is asked twice, and both times
        // against the list the dialog itself builds - legacy entries included, because those
        // are on the list too and belong to nobody. The second reading has to happen *after*
        // the removal; taking it as an argument to the removal call is how the first version
        // got this wrong (arguments are evaluated first, so it always saw the old list).
        bool removed = VisitForgetting.TryRemoveVisit(
            database.VisitsByPlayer, playerKey, destinationKey);
        bool listedNow = IsListedFor(player, destinationKey);
        ForgetOutcome outcome = VisitForgetting.Decide(removed, listedNow);

        if (!removed)
        {
            Debug.Log(
                $"[VisitedTraderTeleport] Nothing to forget for {player.PlayerDisplayName}: " +
                $"{destinationKey} is not one of their visits ({outcome}).");
            return outcome;
        }

        SaveDatabase();
        Debug.Log(
            $"[VisitedTraderTeleport] Forgot {destinationKey} for {player.PlayerDisplayName} " +
            $"({outcome}).");
        return outcome;
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

    // Is this destination on the player's list right now? The same two sources GetDestinations
    // draws on, so the answer cannot disagree with what they are looking at.
    private static bool IsListedFor(EntityPlayer player, string destinationKey)
    {
        return LegacyDestinations.ContainsKey(destinationKey) ||
               GetAllowedNewSchemaKeys(player).Contains(destinationKey);
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
            Position = position.ToPosition3(),
            Forward = Position3.Zero,
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
            Position = player.position.ToPosition3(),
            Forward = Position3.Zero,
            AreaX = report.AreaX,
            AreaZ = report.AreaZ,
            Biome = ResolveBiomeName(biomePosition)
        };
    }

    private static TraderDestination CanonicalizeDestination(TraderDestination destination, Vector3? identityPosition = null)
    {
        return TraderDestinationCanonicalizer.Canonicalize(
            destination, traderAreaLookup, identityPosition?.ToPosition3());
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
            database.Traders[destination.Key] = TraderRecordConverter.ToRecord(destination);
            return true;
        }

        // Keep a previously captured biome if this visit could not resolve one.
        if (string.IsNullOrEmpty(destination.Biome) && !string.IsNullOrEmpty(existing.Biome))
        {
            destination.Biome = existing.Biome;
        }

        bool changed =
            existing.DisplayName != destination.DisplayName ||
            existing.PositionX != destination.Position.X ||
            existing.PositionY != destination.Position.Y ||
            existing.PositionZ != destination.Position.Z ||
            existing.ForwardX != destination.Forward.X ||
            existing.ForwardY != destination.Forward.Y ||
            existing.ForwardZ != destination.Forward.Z ||
            existing.AreaX != destination.AreaX ||
            existing.AreaZ != destination.AreaZ ||
            (existing.Biome ?? string.Empty) != (destination.Biome ?? string.Empty);

        if (changed)
        {
            database.Traders[destination.Key] = TraderRecordConverter.ToRecord(destination);
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

        return TraderRecordConverter.FromRecord(record, key);
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
            TraderDestination destination = TraderRecordConverter.FromRecord(pair.Value, pair.Key);
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
                    canonical = TraderRecordConverter.WithKey(canonical, targetKey);
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

            TraderDestinationRecord canonicalRecord = TraderRecordConverter.ToRecord(canonical);
            bool hasExisting = normalizedTraders.TryGetValue(targetKey, out TraderDestinationRecord existing);
            bool recordChangedFromOriginal = !TraderRecordConverter.RecordsEqual(pair.Value, canonicalRecord);
            if (!hasExisting || keyChanged || !TraderRecordConverter.RecordsEqual(existing, canonicalRecord))
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
            TraderDestination existing = TraderRecordConverter.FromRecord(pair.Value, pair.Key);
            if (TraderMatching.IsSameTrader(existing, destination))
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
            return TraderRecordConverter.WithKey(destination, existingKey);
        }

        return destination;
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
