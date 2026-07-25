using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VisitedTraderTeleport;

internal static class TraderMatching
{
    private const float SameTraderPositionTolerance = 16f;
    private const float SameDetailedTraderPositionTolerance = 6f;

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

    public static List<TraderDestination> DeduplicateDestinations(IEnumerable<TraderDestination> destinations)
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

        float horizontalSqrDistance = HorizontalSqrDistance(destination.Position, currentTrader.Position);
        float tolerance = HasLocalPositionInKey(destination.Key) && HasLocalPositionInKey(currentTrader.Key)
            ? SameDetailedTraderPositionTolerance
            : SameTraderPositionTolerance;
        return horizontalSqrDistance <= tolerance * tolerance;
    }

    // Mirrors the game's "delta.y = 0; delta.sqrMagnitude" pattern without depending on Vector3.
    private static float HorizontalSqrDistance(Position3 a, Position3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    private static bool HasCompatibleTraderIdentity(TraderDestination left, TraderDestination right)
    {
        string leftPrefix = NormalizeTraderIdentityToken(TraderKeyBuilder.GetKeyPrefix(left.Key));
        string rightPrefix = NormalizeTraderIdentityToken(TraderKeyBuilder.GetKeyPrefix(right.Key));
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

        var builder = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
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
}
