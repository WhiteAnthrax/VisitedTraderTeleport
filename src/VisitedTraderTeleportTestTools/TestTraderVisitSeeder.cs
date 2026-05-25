using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace VisitedTraderTeleportTestTools;

internal static class TestTraderVisitSeeder
{
    private const string DatabaseFileName = "VisitedTraderTeleportData.json";
    private const int ChunkViewDim = 3;
    private const float TraderAreaPadding = 8f;
    private const int TraderPositionKeyBucketSize = 4;

    private static readonly HashSet<string> ScansInProgress = new(StringComparer.Ordinal);

    public static void TryStart(EntityPlayerLocal player)
    {
        if (player == null ||
            GameManager.IsDedicatedServer ||
            !TestToolsConfig.RecordAllTradersOnGameStart)
        {
            return;
        }

        string playerKey = GetPlayerKey(player);
        if (string.IsNullOrEmpty(playerKey))
        {
            Debug.LogWarning("[VisitedTraderTeleportTestTools] Could not resolve player key for trader visit seeding.");
            return;
        }

        if (!ScansInProgress.Add(playerKey))
        {
            Debug.Log($"[VisitedTraderTeleportTestTools] Trader visit seed scan already in progress for {player.PlayerDisplayName}.");
            return;
        }

        GameManager gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            ScansInProgress.Remove(playerKey);
            Debug.LogWarning("[VisitedTraderTeleportTestTools] Could not start trader visit seeding because GameManager is unavailable.");
            return;
        }

        gameManager.StartCoroutine(RecordAllKnownTraders(player, playerKey));
    }

    private static IEnumerator RecordAllKnownTraders(EntityPlayerLocal player, string playerKey)
    {
        float startAt = Time.realtimeSinceStartup + TestToolsConfig.StartDelaySeconds;
        while (Time.realtimeSinceStartup < startAt)
        {
            yield return null;
        }

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

            List<TraderArea> traderAreas = world.TraderAreas?
                .Where(traderArea => traderArea != null)
                .OrderBy(traderArea => traderArea.Position.x)
                .ThenBy(traderArea => traderArea.Position.z)
                .ToList();
            if (traderAreas == null || traderAreas.Count == 0)
            {
                Debug.LogWarning("[VisitedTraderTeleportTestTools] No trader areas were available for visit seeding.");
                yield break;
            }

            world.EntityLoadedDelegates += loadedHandler;

            TestVisitedTraderDatabase database = LoadDatabase();
            if (!database.VisitsByPlayer.TryGetValue(playerKey, out HashSet<string> playerVisits))
            {
                playerVisits = new HashSet<string>(StringComparer.Ordinal);
                database.VisitsByPlayer[playerKey] = playerVisits;
            }

            Debug.Log(
                $"[VisitedTraderTeleportTestTools] Trader visit seed scan started for {player.PlayerDisplayName}: " +
                $"areas={traderAreas.Count}, timeoutPerArea={TestToolsConfig.ChunkLoadTimeoutSeconds:0.#}s.");

            bool changed = false;
            int resolvedCount = 0;
            int observedCount = 0;
            int unresolvedCount = 0;
            for (int i = 0; i < traderAreas.Count; i++)
            {
                TraderArea traderArea = traderAreas[i];
                if (TryFindTraderForArea(world, traderArea, loadedTraders, out EntityTrader trader))
                {
                    changed |= RecordTraderDestination(database, trader, traderArea, playerVisits);
                    resolvedCount++;
                    continue;
                }

                Vector3 preloadPosition = GetTraderAreaPreloadPosition(traderArea);
                observer = gameManager.AddChunkObserver(preloadPosition, true, ChunkViewDim, -1);

                Debug.Log(
                    $"[VisitedTraderTeleportTestTools] Trader visit seed preload area {i + 1}/{traderAreas.Count}: " +
                    $"{traderArea.Position.x},{traderArea.Position.z}.");

                float timeoutAt = Time.realtimeSinceStartup + TestToolsConfig.ChunkLoadTimeoutSeconds;
                while (Time.realtimeSinceStartup < timeoutAt &&
                       !TryFindTraderForArea(world, traderArea, loadedTraders, out trader))
                {
                    yield return null;
                }

                if (trader != null)
                {
                    changed |= RecordTraderDestination(database, trader, traderArea, playerVisits);
                    resolvedCount++;
                    observedCount++;
                }
                else
                {
                    unresolvedCount++;
                    Debug.Log(
                        $"[VisitedTraderTeleportTestTools] Trader visit seed unresolved area " +
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
                $"[VisitedTraderTeleportTestTools] Trader visit seed scan: areas={traderAreas.Count}, " +
                $"resolved={resolvedCount}, observed={observedCount}, unresolved={unresolvedCount}, " +
                $"loadedEvents={loadedTraders.Count}, changed={changed}.");

            if (changed)
            {
                SaveDatabase(database);
                Debug.Log($"[VisitedTraderTeleportTestTools] Saved {resolvedCount} seeded trader visits for {player.PlayerDisplayName}.");
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

            ScansInProgress.Remove(playerKey);
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

    private static bool RecordTraderDestination(
        TestVisitedTraderDatabase database,
        EntityTrader trader,
        TraderArea traderArea,
        HashSet<string> playerVisits)
    {
        TestTraderDestination destination = CreateTestDestination(trader, traderArea);
        bool changed = UpsertTrader(database, destination);
        if (playerVisits.Add(destination.Key))
        {
            changed = true;
        }

        return changed;
    }

    private static TestTraderDestination CreateTestDestination(EntityTrader trader, TraderArea traderArea)
    {
        TraderArea resolvedArea = trader.traderArea ?? traderArea;
        Vector3 identityPosition = trader.position;
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

        Vector3 destinationPosition = trader.position + forward * 2f;
        int areaX = resolvedArea?.Position.x ?? Mathf.RoundToInt(destinationPosition.x);
        int areaZ = resolvedArea?.Position.z ?? Mathf.RoundToInt(destinationPosition.z);
        string keyPrefix = GetKeyPrefix(GetKey(trader, resolvedArea));
        if (string.IsNullOrEmpty(keyPrefix))
        {
            keyPrefix = "trader";
        }

        string key = resolvedArea == null
            ? GetKey(trader, resolvedArea)
            : BuildCanonicalKey(keyPrefix, resolvedArea, identityPosition);

        return new TestTraderDestination
        {
            Key = key,
            DisplayName = GetDisplayName(trader),
            Position = destinationPosition,
            Forward = Vector3.zero,
            AreaX = areaX,
            AreaZ = areaZ
        };
    }

    private static bool UpsertTrader(TestVisitedTraderDatabase database, TestTraderDestination destination)
    {
        if (!database.Traders.TryGetValue(destination.Key, out TestTraderDestinationRecord existing))
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

    private static TestTraderDestinationRecord ToRecord(TestTraderDestination destination)
    {
        return new TestTraderDestinationRecord
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

    private static TestVisitedTraderDatabase LoadDatabase()
    {
        string path = Path.Combine(GetSaveDirectory(), DatabaseFileName);
        if (!File.Exists(path))
        {
            return new TestVisitedTraderDatabase();
        }

        try
        {
            TestVisitedTraderDatabase loaded = JsonConvert.DeserializeObject<TestVisitedTraderDatabase>(File.ReadAllText(path));
            TestVisitedTraderDatabase database = loaded ?? new TestVisitedTraderDatabase();
            database.Traders ??= new Dictionary<string, TestTraderDestinationRecord>();
            database.VisitsByPlayer ??= new Dictionary<string, HashSet<string>>();
            return database;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleportTestTools] Could not read visited trader database: {ex.Message}");
            return new TestVisitedTraderDatabase();
        }
    }

    private static void SaveDatabase(TestVisitedTraderDatabase database)
    {
        string saveDirectory = GetSaveDirectory();
        Directory.CreateDirectory(saveDirectory);
        File.WriteAllText(
            Path.Combine(saveDirectory, DatabaseFileName),
            JsonConvert.SerializeObject(database, Formatting.Indented));
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
            Debug.LogWarning($"[VisitedTraderTeleportTestTools] Could not resolve save directory: {ex.Message}");
        }

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods", "VisitedTraderTeleport");
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
        size.x += TraderAreaPadding * 2f;
        size.y += 64f;
        size.z += TraderAreaPadding * 2f;
        return new Bounds(center, size);
    }
}

internal sealed class TestVisitedTraderDatabase
{
    public int SchemaVersion = 1;
    public Dictionary<string, TestTraderDestinationRecord> Traders = new();
    public Dictionary<string, HashSet<string>> VisitsByPlayer = new();
}

internal sealed class TestTraderDestinationRecord
{
    public string Key;
    public string DisplayName;
    public float PositionX;
    public float PositionY;
    public float PositionZ;
    public float ForwardX;
    public float ForwardY;
    public float ForwardZ;
    public int AreaX;
    public int AreaZ;
}

internal sealed class TestTraderDestination
{
    public string Key;
    public string DisplayName;
    public Vector3 Position;
    public Vector3 Forward;
    public int AreaX;
    public int AreaZ;
}
