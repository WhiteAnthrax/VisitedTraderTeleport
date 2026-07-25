namespace VisitedTraderTeleport;

internal static class TraderKeyBuilder
{
    public static string GetKeyPrefix(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        int separator = key.IndexOf(':');
        return (separator > 0 ? key.Substring(0, separator) : key).Trim().ToLowerInvariant();
    }

    // localX/localZ are expected to already be quantized (see ITraderAreaLookup).
    public static string BuildCanonicalKey(string keyPrefix, int areaX, int areaZ, int localX, int localZ)
    {
        return $"{keyPrefix}:{areaX}:{areaZ}:{localX}:{localZ}";
    }
}
