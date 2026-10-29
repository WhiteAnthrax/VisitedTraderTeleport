using System;
using System.Globalization;
using System.Text;

namespace VisitedTraderTeleport;

internal static class TraderNameFormatter
{
    public static string Format(string rawName, ILocalizationProvider localization)
    {
        string cleaned = CleanTraderName(rawName);
        string key = GetKnownTraderNameKey(cleaned);
        string gameName = GetGameName(rawName, localization);
        if (!string.IsNullOrEmpty(key))
        {
            // Keep the short name while the game still calls the trader by it ("Trader Rekt"),
            // but follow an overhaul that renames them (Asylum's Rekt is "Lee Gon"), so the list
            // matches the name the trader dialog shows.
            string shortName = localization.Get(key);
            return string.IsNullOrEmpty(gameName) || gameName.IndexOf(shortName, StringComparison.OrdinalIgnoreCase) >= 0
                ? shortName
                : gameName;
        }

        if (!string.IsNullOrEmpty(gameName))
        {
            return gameName;
        }

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return localization.Get("vtt_trader_name_generic");
        }

        return localization.Format("vtt_trader_name_unknown", ToTitleWords(cleaned));
    }

    // Recorded names are usually entity class names such as npcTraderRekt, which are also the
    // game's localization keys for the trader's name. A name that is not a key comes back as-is.
    private static string GetGameName(string rawName, ILocalizationProvider localization)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return string.Empty;
        }

        string trimmed = rawName.Trim();
        string localized = localization.Get(trimmed);
        return string.IsNullOrWhiteSpace(localized) || localized == trimmed
            ? string.Empty
            : localized.Trim();
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

    public static string ToTitleWords(string value)
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
}
