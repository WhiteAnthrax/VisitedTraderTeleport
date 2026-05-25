using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VisitedTraderTeleportTestTools;

[HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.OnAddedToWorld))]
internal static class EntityAliveOnAddedToWorldPatch
{
    public static void Postfix(EntityAlive __instance)
    {
        if (__instance is EntityPlayerLocal localPlayer)
        {
            TestTraderBootstrap.TryStart(localPlayer);
            TestTraderVisitSeeder.TryStart(localPlayer);
        }
    }
}

internal static class TestTraderBootstrap
{
    private const int ChunkViewDim = 3;
    private const float TraderAreaPadding = 8f;
    private static readonly HashSet<int> StartedPlayers = new();

    public static void TryStart(EntityPlayerLocal player)
    {
        if (player == null ||
            GameManager.IsDedicatedServer ||
            !TestToolsConfig.TeleportToTraderOnGameStart)
        {
            return;
        }

        if (!StartedPlayers.Add(player.entityId))
        {
            return;
        }

        GameManager gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            Debug.LogWarning("[VisitedTraderTeleportTestTools] Could not start trader bootstrap because GameManager is unavailable.");
            return;
        }

        gameManager.StartCoroutine(TeleportToTraderWhenReady(player));
    }

    private static IEnumerator TeleportToTraderWhenReady(EntityPlayerLocal player)
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
        if (player == null || gameManager == null || world == null)
        {
            yield break;
        }

        try
        {
            world.EntityLoadedDelegates += loadedHandler;

            List<TraderArea> traderAreas = SelectTraderAreas(world, player.position);
            if (traderAreas.Count == 0)
            {
                Debug.LogWarning("[VisitedTraderTeleportTestTools] No trader areas were available for game-start teleport.");
                yield break;
            }

            foreach (TraderArea traderArea in traderAreas)
            {
                Vector3 preloadPosition = GetTraderAreaPreloadPosition(traderArea);
                ChunkManager.ChunkObserver observer = null;
                try
                {
                    observer = gameManager.AddChunkObserver(preloadPosition, true, ChunkViewDim, -1);
                    Debug.Log(
                        $"[VisitedTraderTeleportTestTools] Preloading trader area " +
                        $"{traderArea.Position.x},{traderArea.Position.z} for game-start teleport.");

                    EntityTrader trader = null;
                    float timeoutAt = Time.realtimeSinceStartup + TestToolsConfig.ChunkLoadTimeoutSeconds;
                    while (player != null &&
                           Time.realtimeSinceStartup < timeoutAt &&
                           !TryFindTraderForArea(world, traderArea, loadedTraders, out trader))
                    {
                        yield return null;
                    }

                    if (player == null)
                    {
                        yield break;
                    }

                    if (trader == null)
                    {
                        if (TestToolsConfig.FallbackToTraderAreaCenter)
                        {
                            Debug.Log(
                                $"[VisitedTraderTeleportTestTools] Could not resolve trader in area " +
                                $"{traderArea.Position.x},{traderArea.Position.z}; using area-center fallback.");
                            TeleportToAreaCenter(player, traderArea);
                            yield break;
                        }

                        Debug.Log(
                            $"[VisitedTraderTeleportTestTools] Could not resolve trader in area " +
                            $"{traderArea.Position.x},{traderArea.Position.z}; trying next trader area.");
                        continue;
                    }

                    Vector3 target = GetTraderFrontPosition(trader);
                    player.TeleportToPosition(target, false, null);
                    GameManager.ShowTooltip(player, "VTT test tools: moved to a trader.", false, false, 4f);
                    Debug.Log(
                        $"[VisitedTraderTeleportTestTools] Teleported {player.PlayerDisplayName} to trader " +
                        $"at ({target.x:0.##}, {target.y:0.##}, {target.z:0.##}); loadedEvents={loadedTraders.Count}.");
                    yield break;
                }
                finally
                {
                    if (observer != null)
                    {
                        gameManager?.RemoveChunkObserver(observer);
                    }
                }
            }

            Debug.LogWarning(
                $"[VisitedTraderTeleportTestTools] No trader NPCs could be resolved; " +
                $"loadedEvents={loadedTraders.Count}; game-start teleport skipped.");
        }
        finally
        {
            world.EntityLoadedDelegates -= loadedHandler;
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

    private static List<TraderArea> SelectTraderAreas(World world, Vector3 playerPosition)
    {
        List<TraderArea> traderAreas = world?.TraderAreas?
            .Where(traderArea => traderArea != null)
            .ToList();
        if (traderAreas == null || traderAreas.Count == 0)
        {
            return new List<TraderArea>();
        }

        if (TestToolsConfig.TargetTrader == TargetTraderMode.First)
        {
            return traderAreas
                .OrderBy(traderArea => traderArea.Position.x)
                .ThenBy(traderArea => traderArea.Position.z)
                .ToList();
        }

        return traderAreas
            .OrderBy(traderArea => HorizontalDistanceSq(GetTraderAreaPreloadPosition(traderArea), playerPosition))
            .ThenBy(traderArea => traderArea.Position.x)
            .ThenBy(traderArea => traderArea.Position.z)
            .ToList();
    }

    private static float HorizontalDistanceSq(Vector3 left, Vector3 right)
    {
        float x = left.x - right.x;
        float z = left.z - right.z;
        return x * x + z * z;
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

    private static Vector3 GetTraderFrontPosition(EntityTrader trader)
    {
        Vector3 forward = trader.GetForwardVector();
        forward.y = 0f;
        if (forward.sqrMagnitude >= 0.001f)
        {
            forward.Normalize();
        }
        else
        {
            forward = Vector3.forward;
        }

        Vector3 position = trader.position + forward * 2f;
        position.y += 0.25f;
        return position;
    }

    private static void TeleportToAreaCenter(EntityPlayerLocal player, TraderArea traderArea)
    {
        Vector3 target = GetTraderAreaPreloadPosition(traderArea);
        target.y += 1f;
        player.TeleportToPosition(target, false, null);
        GameManager.ShowTooltip(player, "VTT test tools: moved to a trader area.", false, false, 4f);
        Debug.Log(
            $"[VisitedTraderTeleportTestTools] Fallback teleported {player.PlayerDisplayName} to trader area " +
            $"{traderArea.Position.x},{traderArea.Position.z} at " +
            $"({target.x:0.##}, {target.y:0.##}, {target.z:0.##}).");
    }
}
