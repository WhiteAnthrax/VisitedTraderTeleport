using System;
using System.Globalization;
using UnityEngine;

namespace VisitedTraderTeleport;

internal sealed class TraderDestination
{
    public string Key;
    public string DisplayName;
    public Vector3 Position;
    public Vector3 Forward;
    public int AreaX;
    public int AreaZ;

    public string DialogText =>
        $"{DisplayName} ({Mathf.RoundToInt(Position.x)}, {Mathf.RoundToInt(Position.z)})";

    public string Serialize()
    {
        return string.Join("|",
            Escape(Key),
            Escape(DisplayName),
            Position.x.ToString(CultureInfo.InvariantCulture),
            Position.y.ToString(CultureInfo.InvariantCulture),
            Position.z.ToString(CultureInfo.InvariantCulture),
            Forward.x.ToString(CultureInfo.InvariantCulture),
            Forward.y.ToString(CultureInfo.InvariantCulture),
            Forward.z.ToString(CultureInfo.InvariantCulture),
            AreaX.ToString(CultureInfo.InvariantCulture),
            AreaZ.ToString(CultureInfo.InvariantCulture));
    }

    public static bool TryParse(string line, out TraderDestination destination)
    {
        destination = null;
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = SplitEscaped(line);
        if (parts.Length < 7 ||
            !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
            !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
        {
            return false;
        }

        Vector3 forward = Vector3.forward;
        int areaIndex = 5;
        if (parts.Length >= 10 &&
            float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float forwardX) &&
            float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float forwardY) &&
            float.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out float forwardZ))
        {
            forward = new Vector3(forwardX, forwardY, forwardZ);
            areaIndex = 8;
        }

        if (parts.Length <= areaIndex + 1 ||
            !int.TryParse(parts[areaIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out int areaX) ||
            !int.TryParse(parts[areaIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int areaZ))
        {
            return false;
        }

        destination = new TraderDestination
        {
            Key = Unescape(parts[0]),
            DisplayName = Unescape(parts[1]),
            Position = new Vector3(x, y, z),
            Forward = forward,
            AreaX = areaX,
            AreaZ = areaZ
        };
        return !string.IsNullOrEmpty(destination.Key);
    }

    private static string Escape(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("|", "\\p")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    private static string Unescape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\\p", "|")
            .Replace("\\\\", "\\");
    }

    private static string[] SplitEscaped(string line)
    {
        var parts = new System.Collections.Generic.List<string>();
        var current = new System.Text.StringBuilder();
        bool escaped = false;

        foreach (char c in line)
        {
            if (escaped)
            {
                current.Append('\\');
                current.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == '|')
            {
                parts.Add(current.ToString());
                current.Length = 0;
                continue;
            }

            current.Append(c);
        }

        if (escaped)
        {
            current.Append('\\');
        }

        parts.Add(current.ToString());
        return parts.ToArray();
    }
}
