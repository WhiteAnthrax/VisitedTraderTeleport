using System.Collections.Generic;
using UnityEngine;

namespace VisitedTraderTeleport;

internal sealed class GameTraderAreaLookup : ITraderAreaLookup
{
    private const float TestTraderAreaPadding = 8f;
    private const int TraderPositionKeyBucketSize = 4;

    public bool TryFindTraderArea(Position3 position, out int areaX, out int areaZ, out int localX, out int localZ)
    {
        areaX = 0;
        areaZ = 0;
        localX = 0;
        localZ = 0;

        Vector3 vectorPosition = position.ToVector3();
        TraderArea traderArea = FindTraderAreaForPosition(vectorPosition);
        if (traderArea == null)
        {
            return false;
        }

        areaX = traderArea.Position.x;
        areaZ = traderArea.Position.z;
        localX = QuantizeTraderLocalPosition(vectorPosition.x - traderArea.Position.x);
        localZ = QuantizeTraderLocalPosition(vectorPosition.z - traderArea.Position.z);
        return true;
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
}
