using System;

namespace VisitedTraderTeleport;

internal static class TraderDestinationCanonicalizer
{
    public static TraderDestination Canonicalize(
        TraderDestination destination, ITraderAreaLookup traderAreaLookup, Position3? identityPosition = null)
    {
        if (destination == null)
        {
            return null;
        }

        Position3 keyPosition = identityPosition ?? destination.Position;
        bool found = traderAreaLookup.TryFindTraderArea(keyPosition, out int areaX, out int areaZ, out int localX, out int localZ);
        if (!found)
        {
            found = traderAreaLookup.TryFindTraderArea(destination.Position, out areaX, out areaZ, out localX, out localZ);
        }

        if (!found)
        {
            return destination;
        }

        string keyPrefix = TraderKeyBuilder.GetKeyPrefix(destination.Key);
        if (string.IsNullOrEmpty(keyPrefix))
        {
            keyPrefix = "trader";
        }

        string canonicalKey = TraderKeyBuilder.BuildCanonicalKey(keyPrefix, areaX, areaZ, localX, localZ);
        if (string.Equals(destination.Key, canonicalKey, StringComparison.Ordinal) &&
            destination.AreaX == areaX &&
            destination.AreaZ == areaZ)
        {
            return destination;
        }

        return new TraderDestination
        {
            Key = canonicalKey,
            DisplayName = destination.DisplayName,
            Position = destination.Position,
            Forward = destination.Forward,
            AreaX = areaX,
            AreaZ = areaZ,
            Biome = destination.Biome
        };
    }
}
