using System;
using System.Collections;
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
    private const float TestTraderPreloadTimeoutSeconds = 8f;
    private const int TestTraderPreloadViewDim = 3;
    private const float TestTraderAreaPadding = 8f;

    private static readonly Dictionary<string, TraderDestination> LegacyDestinations = new();
    private static readonly HashSet<string> TestScansInProgress = new(StringComparer.Ordinal);
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

        return destination.AreaX == currentTrader.AreaX &&
               destination.AreaZ == currentTrader.AreaZ;
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
            StartRecordAllKnownTradersForTesting(player);
            return;
        }

        SaveDatabase();
        Debug.Log($"[VisitedTraderTeleport] Recorded visited trader for {player.PlayerDisplayName}: {destination.DialogText}");
        StartRecordAllKnownTradersForTesting(player);
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
            StartRecordAllKnownTradersForTesting(player);
            return;
        }

        SaveDatabase();
        Debug.Log($"[VisitedTraderTeleport] Recorded reported visited trader for {player.PlayerDisplayName}: {destination.DialogText}");
        StartRecordAllKnownTradersForTesting(player);
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

    private static void StartRecordAllKnownTradersForTesting(EntityPlayer player)
    {
        if (!VisitedTraderTeleportConfig.TestRecordAllTradersOnVisit)
        {
            return;
        }

        EnsureLoaded();

        string playerKey = GetPlayerKey(player);
        if (string.IsNullOrEmpty(playerKey))
        {
            Debug.LogWarning("[VisitedTraderTeleport] Test mode could not resolve player key.");
            return;
        }

        if (!TestScansInProgress.Add(playerKey))
        {
            Debug.Log($"[VisitedTraderTeleport] Test mode scan already in progress for {player.PlayerDisplayName}.");
            return;
        }

        GameManager gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            TestScansInProgress.Remove(playerKey);
            Debug.LogWarning("[VisitedTraderTeleport] Test mode could not start because GameManager is unavailable.");
            return;
        }

        gameManager.StartCoroutine(RecordAllKnownTradersForTesting(player, playerKey));
    }

    private static IEnumerator RecordAllKnownTradersForTesting(EntityPlayer player, string playerKey)
    {
        GameManager gameManager = GameManager.Instance;
        World world = gameManager?.World;
        var loadedTraderIds = new HashSet<int>();
        var loadedTraders = new List<EntityTrader>();
        World.OnEntityLoadedDelegate loadedHandler = CaptureLoadedTrader;
        ChunkManager.ChunkObserver observer = null;

        try
        {
            if (gameManager == null || world == null || player == null)
            {
                yield break;
            }

            List<TraderArea> traderAreas = world?.TraderAreas?
                .Where(traderArea => traderArea != null)
                .ToList();
            if (traderAreas == null || traderAreas.Count == 0)
            {
                Debug.LogWarning("[VisitedTraderTeleport] Test mode found no trader areas to record.");
                yield break;
            }

            world.EntityLoadedDelegates += loadedHandler;

            if (!database.VisitsByPlayer.TryGetValue(playerKey, out HashSet<string> playerVisits))
            {
                playerVisits = new HashSet<string>(StringComparer.Ordinal);
                database.VisitsByPlayer[playerKey] = playerVisits;
            }

            Debug.Log(
                $"[VisitedTraderTeleport] Test mode sequential scan started for {player.PlayerDisplayName}: " +
                $"areas={traderAreas.Count}, timeoutPerArea={TestTraderPreloadTimeoutSeconds:0.#}s.");

            bool changed = false;
            int resolvedCount = 0;
            int observedCount = 0;
            int unresolvedCount = 0;
            for (int i = 0; i < traderAreas.Count; i++)
            {
                TraderArea traderArea = traderAreas[i];
                if (TryFindTraderForArea(world, traderArea, loadedTraders, out EntityTrader trader))
                {
                    changed |= RecordTestTraderDestination(trader, traderArea, playerVisits);
                    resolvedCount++;
                    continue;
                }

                Vector3 preloadPosition = GetTraderAreaPreloadPosition(traderArea);
                observer = gameManager.AddChunkObserver(
                    preloadPosition,
                    !GameManager.IsDedicatedServer,
                    TestTraderPreloadViewDim,
                    -1);

                Debug.Log(
                    $"[VisitedTraderTeleport] Test mode preload area {i + 1}/{traderAreas.Count}: " +
                    $"{traderArea.Position.x},{traderArea.Position.z}.");

                float timeoutAt = Time.realtimeSinceStartup + TestTraderPreloadTimeoutSeconds;
                while (Time.realtimeSinceStartup < timeoutAt &&
                       !TryFindTraderForArea(world, traderArea, loadedTraders, out trader))
                {
                    yield return null;
                }

                if (trader != null)
                {
                    changed |= RecordTestTraderDestination(trader, traderArea, playerVisits);
                    resolvedCount++;
                    observedCount++;
                }
                else
                {
                    unresolvedCount++;
                    Debug.Log(
                        $"[VisitedTraderTeleport] Test mode unresolved trader area " +
                        $"{traderArea.Position.x},{traderArea.Position.z} after preload.");
                }

                if (observer != null)
                {
                    gameManager.RemoveChunkObserver(observer);
                    observer = null;
                }

                yield return null;
            }

            Debug.Log(
                $"[VisitedTraderTeleport] Test mode scan: areas={traderAreas.Count}, " +
                $"resolved={resolvedCount}, observed={observedCount}, unresolved={unresolvedCount}, " +
                $"loadedEvents={loadedTraders.Count}, changed={changed}.");

            if (changed)
            {
                SaveDatabase();
                Debug.Log($"[VisitedTraderTeleport] Test mode saved {resolvedCount} known traders for {player.PlayerDisplayName}.");
            }

            ClientInfo clientInfo = ConnectionManager.Instance?.Clients?.ForEntityId(player.entityId);
            if (clientInfo != null)
            {
                VisitedTraderNetwork.SendSnapshot(clientInfo);
            }
        }
        finally
        {
            if (world != null)
            {
                world.EntityLoadedDelegates -= loadedHandler;
            }

            if (observer != null)
            {
                gameManager?.RemoveChunkObserver(observer);
            }

            TestScansInProgress.Remove(playerKey);
        }

        void CaptureLoadedTrader(Entity entity)
        {
            if (entity is not EntityTrader trader)
            {
                return;
            }

            if (loadedTraderIds.Add(trader.entityId))
            {
                loadedTraders.Add(trader);
            }
        }
    }

    private static Vector3 GetTraderAreaPreloadPosition(TraderArea traderArea)
    {
        Vector3 position = traderArea.Position;
        Vector3i size = traderArea.PrefabSize;
        position.x += size.x * 0.5f;
        position.z += size.z * 0.5f;
        return position;
    }

    private static bool TryFindTraderForArea(
        World world,
        TraderArea traderArea,
        IReadOnlyList<EntityTrader> loadedTraders,
        out EntityTrader trader)
    {
        trader = null;
        if (world == null || traderArea == null)
        {
            return false;
        }

        if (IsTraderForArea(traderArea.owningTrader, traderArea))
        {
            trader = traderArea.owningTrader;
            return true;
        }

        if (loadedTraders != null)
        {
            for (int i = loadedTraders.Count - 1; i >= 0; i--)
            {
                EntityTrader loadedTrader = loadedTraders[i];
                if (IsTraderForArea(loadedTrader, traderArea))
                {
                    trader = loadedTrader;
                    return true;
                }
            }
        }

        var entities = new List<Entity>();
        foreach (Entity entity in world.GetEntitiesInBounds(typeof(EntityTrader), GetTraderAreaBounds(traderArea), entities))
        {
            if (entity is EntityTrader foundTrader && IsTraderForArea(foundTrader, traderArea))
            {
                trader = foundTrader;
                return true;
            }
        }

        return false;
    }

    private static bool IsTraderForArea(EntityTrader trader, TraderArea traderArea)
    {
        if (trader == null || traderArea == null || trader.isUnloaded)
        {
            return false;
        }

        if (IsSameTraderArea(trader.traderArea, traderArea))
        {
            return true;
        }

        return GetTraderAreaBounds(traderArea).Contains(trader.position);
    }

    private static bool IsSameTraderArea(TraderArea left, TraderArea right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        return ReferenceEquals(left, right) ||
               (left.Position == right.Position && left.PrefabSize == right.PrefabSize);
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

    private static bool RecordTestTraderDestination(
        EntityTrader trader,
        TraderArea traderArea,
        HashSet<string> playerVisits)
    {
        TraderDestination destination = CreateTestDestination(trader, traderArea);
        bool changed = UpsertTrader(destination);
        if (playerVisits.Add(destination.Key))
        {
            changed = true;
        }

        return changed;
    }

    private static TraderDestination CreateTestDestination(EntityTrader trader, TraderArea traderArea)
    {
        Vector3 forward = trader.GetForwardVector();
        forward.y = 0f;
        if (forward.sqrMagnitude >= 0.001f)
        {
            forward.Normalize();
        }
        else
        {
            forward = Vector3.zero;
        }

        Vector3 position = trader.position + forward * 2f;
        int areaX = Mathf.RoundToInt(position.x);
        int areaZ = Mathf.RoundToInt(position.z);

        TraderArea resolvedArea = trader.traderArea ?? traderArea;
        if (resolvedArea != null)
        {
            areaX = resolvedArea.Position.x;
            areaZ = resolvedArea.Position.z;
        }

        return new TraderDestination
        {
            Key = GetKey(trader, resolvedArea),
            DisplayName = GetDisplayName(trader),
            Position = position,
            Forward = Vector3.zero,
            AreaX = areaX,
            AreaZ = areaZ
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
