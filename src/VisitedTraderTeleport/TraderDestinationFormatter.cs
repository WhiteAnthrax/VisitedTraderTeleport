using System;
using System.Globalization;
using UnityEngine;

namespace VisitedTraderTeleport;

internal static class TraderDestinationFormatter
{
    private const float MetersPerKilometer = 1000f;

    private static readonly string[] DirectionKeys =
    {
        "vtt_direction_n",
        "vtt_direction_ne",
        "vtt_direction_e",
        "vtt_direction_se",
        "vtt_direction_s",
        "vtt_direction_sw",
        "vtt_direction_w",
        "vtt_direction_nw"
    };

    public static string FormatResponse(TraderDestination destination, EntityPlayer player)
    {
        if (destination == null)
        {
            return string.Empty;
        }

        string name = FormatName(destination);
        string distance = FormatDistance(destination, player);
        string direction = FormatDirection(destination, player);
        string coordinates = FormatCoordinates(destination);

        // The per-destination travel cost is shown on the confirmation screen and as a
        // rate in the status line, so list entries stay consistent for free and paid trips.
        string entry = VTTLocalization.Format("vtt_destination_response", name, distance, direction, coordinates);
        return entry + FormatBiomeSuffix(destination);
    }

    public static string FormatName(TraderDestination destination)
    {
        return TraderNameFormatter.Format(destination?.DisplayName, GameLocalizationProvider.Instance);
    }

    public static string FormatBiomeSuffix(TraderDestination destination)
    {
        string label = GetBiomeLabel(destination);
        return string.IsNullOrEmpty(label)
            ? string.Empty
            : VTTLocalization.Format("vtt_destination_biome_suffix", label);
    }

    private static string GetBiomeLabel(TraderDestination destination)
    {
        if (destination == null)
        {
            return string.Empty;
        }

        // Prefer the biome captured when the trader was visited. Fall back to a live lookup
        // only for destinations recorded before biomes were stored (works when loaded).
        string biomeName = string.IsNullOrWhiteSpace(destination.Biome)
            ? ResolveBiomeNameFromWorld(destination.Position.ToVector3())
            : destination.Biome;
        if (string.IsNullOrWhiteSpace(biomeName))
        {
            return string.Empty;
        }

        string key = GetKnownBiomeKey(biomeName);
        return string.IsNullOrEmpty(key)
            ? ClampBiomeLabel(TraderNameFormatter.ToTitleWords(biomeName.Replace('_', ' ').Replace('-', ' ')))
            : VTTLocalization.Get(key);
    }

    private static string ResolveBiomeNameFromWorld(Vector3 position)
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
            Debug.LogWarning($"[VisitedTraderTeleport] Could not resolve biome for destination: {ex.Message}");
            return string.Empty;
        }
    }

    // Keep an unrecognized modded biome name from stretching the list line.
    private static string ClampBiomeLabel(string label)
    {
        const int maxLength = 24;
        if (string.IsNullOrEmpty(label) || label.Length <= maxLength)
        {
            return label;
        }

        return label.Substring(0, maxLength - 1).TrimEnd() + "…";
    }

    private static string GetKnownBiomeKey(string biomeName)
    {
        switch ((biomeName ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "forest":
            case "pine_forest":
                return "vtt_biome_forest";
            case "desert":
                return "vtt_biome_desert";
            case "snow":
                return "vtt_biome_snow";
            case "wasteland":
                return "vtt_biome_wasteland";
            case "burnt_forest":
                return "vtt_biome_burnt_forest";
            case "underwater":
            case "water":
                return "vtt_biome_water";
            default:
                return string.Empty;
        }
    }

    public static string FormatTransportDestination(TraderDestination destination)
    {
        if (destination == null)
        {
            return VTTLocalization.Get("vtt_trader_name_generic");
        }

        return VTTLocalization.Format(
            "vtt_transport_destination",
            FormatName(destination),
            FormatCoordinates(destination));
    }

    private static string FormatDistance(TraderDestination destination, EntityPlayer player)
    {
        if (destination == null || player == null)
        {
            return VTTLocalization.Get("vtt_distance_unknown");
        }

        Vector3 delta = destination.Position.ToVector3() - player.position;
        delta.y = 0f;
        float meters = delta.magnitude;
        if (meters >= MetersPerKilometer)
        {
            string kilometers = (meters / MetersPerKilometer).ToString("0.0", CultureInfo.InvariantCulture);
            return VTTLocalization.Format("vtt_distance_km", kilometers);
        }

        return VTTLocalization.Format("vtt_distance_m", Mathf.RoundToInt(meters));
    }

    private static string FormatDirection(TraderDestination destination, EntityPlayer player)
    {
        if (destination == null || player == null)
        {
            return VTTLocalization.Get("vtt_direction_unknown");
        }

        Vector3 delta = destination.Position.ToVector3() - player.position;
        delta.y = 0f;
        if (delta.sqrMagnitude < 1f)
        {
            return VTTLocalization.Get("vtt_direction_here");
        }

        float angle = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
        if (angle < 0f)
        {
            angle += 360f;
        }

        int index = Mathf.RoundToInt(angle / 45f) % DirectionKeys.Length;
        return VTTLocalization.Get(DirectionKeys[index]);
    }

    private static string FormatCoordinates(TraderDestination destination)
    {
        int x = Mathf.RoundToInt(destination.Position.X);
        int z = Mathf.RoundToInt(destination.Position.Z);

        return VTTLocalization.Format(
            "vtt_coordinates",
            Math.Abs(x),
            VTTLocalization.Get(x >= 0 ? "vtt_coord_east" : "vtt_coord_west"),
            Math.Abs(z),
            VTTLocalization.Get(z >= 0 ? "vtt_coord_south" : "vtt_coord_north"));
    }

    public static float GetDistanceSq(TraderDestination destination, EntityPlayer player)
    {
        if (destination == null || player == null)
        {
            return float.MaxValue;
        }

        Vector3 delta = destination.Position.ToVector3() - player.position;
        delta.y = 0f;
        return delta.sqrMagnitude;
    }
}
