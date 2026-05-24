using System;
using System.Globalization;
using System.Text;
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
        string cost = TravelCostService.FormatCostSuffix(destination, player);

        return VTTLocalization.Format("vtt_destination_response", name, distance, direction, coordinates) + cost;
    }

    public static string FormatName(TraderDestination destination)
    {
        return FormatTraderName(destination?.DisplayName);
    }

    private static string FormatDistance(TraderDestination destination, EntityPlayer player)
    {
        if (destination == null || player == null)
        {
            return VTTLocalization.Get("vtt_distance_unknown");
        }

        Vector3 delta = destination.Position - player.position;
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

        Vector3 delta = destination.Position - player.position;
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
        int x = Mathf.RoundToInt(destination.Position.x);
        int z = Mathf.RoundToInt(destination.Position.z);

        return VTTLocalization.Format(
            "vtt_coordinates",
            Math.Abs(x),
            VTTLocalization.Get(x >= 0 ? "vtt_coord_east" : "vtt_coord_west"),
            Math.Abs(z),
            VTTLocalization.Get(z >= 0 ? "vtt_coord_south" : "vtt_coord_north"));
    }

    private static string FormatTraderName(string rawName)
    {
        string cleaned = CleanTraderName(rawName);
        string key = GetKnownTraderNameKey(cleaned);
        if (!string.IsNullOrEmpty(key))
        {
            return VTTLocalization.Get(key);
        }

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return VTTLocalization.Get("vtt_trader_name_generic");
        }

        return VTTLocalization.Format("vtt_trader_name_unknown", ToTitleWords(cleaned));
    }

    private static string GetKnownTraderNameKey(string cleaned)
    {
        switch (NormalizeNameToken(cleaned))
        {
            case "rekt":
                return "vtt_trader_name_rekt";
            case "jen":
                return "vtt_trader_name_jen";
            case "bob":
                return "vtt_trader_name_bob";
            case "hugh":
                return "vtt_trader_name_hugh";
            case "joel":
                return "vtt_trader_name_joel";
            case "gene":
                return "vtt_trader_name_gene";
            case "johnny":
                return "vtt_trader_name_johnny";
            case "radcat":
            case "spheretest":
                return "vtt_trader_name_radcat";
            default:
                return string.Empty;
        }
    }

    private static string CleanTraderName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return string.Empty;
        }

        string cleaned = rawName.Trim();
        if (cleaned.StartsWith("npcTrader", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring("npcTrader".Length);
        }
        else if (cleaned.StartsWith("trader_", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring("trader_".Length);
        }
        else if (cleaned.StartsWith("Trader ", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring("Trader ".Length);
        }
        else if (cleaned.StartsWith("trader", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring("trader".Length);
        }

        return cleaned
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Trim();
    }

    private static string NormalizeNameToken(string value)
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

    private static string ToTitleWords(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        char previous = '\0';
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsWhiteSpace(c))
            {
                if (builder.Length > 0 && builder[builder.Length - 1] != ' ')
                {
                    builder.Append(' ');
                }

                previous = c;
                continue;
            }

            if (i > 0 &&
                char.IsUpper(c) &&
                (char.IsLower(previous) || char.IsDigit(previous)) &&
                builder.Length > 0 &&
                builder[builder.Length - 1] != ' ')
            {
                builder.Append(' ');
            }

            builder.Append(c);
            previous = c;
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(builder.ToString().Trim().ToLowerInvariant());
    }

    public static float GetDistanceSq(TraderDestination destination, EntityPlayer player)
    {
        if (destination == null || player == null)
        {
            return float.MaxValue;
        }

        Vector3 delta = destination.Position - player.position;
        delta.y = 0f;
        return delta.sqrMagnitude;
    }
}
