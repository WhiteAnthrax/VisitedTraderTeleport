using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using UnityEngine;

namespace VisitedTraderTeleport;

internal static class VisitedTraderTeleportConfig
{
    private const string ConfigFileName = "VisitedTraderTeleport.xml";
    private const float MaxTravelCostPerMeter = 1000f;
    private const int MaxTravelCostMinimum = 1000000;
    private const float MaxTravelTransitionDurationSeconds = 60f;
    private const float MaxTravelSoundRepeatSeconds = 60f;
    private static string modPath;
    private static string loadedPath;
    private static AccessMode loadedMode = AccessMode.Personal;
    private static TravelCostSettings loadedTravelCost = TravelCostSettings.Disabled();
    private static TravelTransitionSettings loadedTravelTransition = TravelTransitionSettings.Default();

    public static void Configure(Mod mod)
    {
        modPath = mod?.Path;
        loadedPath = null;
    }

    public static AccessMode AccessMode
    {
        get
        {
            EnsureLoaded();
            return loadedMode;
        }
    }

    public static TravelCostSettings TravelCost
    {
        get
        {
            EnsureLoaded();
            return loadedTravelCost;
        }
    }

    public static TravelTransitionSettings TravelTransition
    {
        get
        {
            EnsureLoaded();
            return loadedTravelTransition;
        }
    }

    private static void EnsureLoaded()
    {
        string path = GetConfigPath();
        if (string.Equals(path, loadedPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        loadedPath = path;
        loadedMode = AccessMode.Personal;
        loadedTravelCost = TravelCostSettings.Disabled();
        loadedTravelTransition = TravelTransitionSettings.Default();

        try
        {
            if (!File.Exists(path))
            {
                Debug.Log($"[VisitedTraderTeleport] Config not found, using default access mode: {loadedMode}.");
                return;
            }

            XDocument doc = XDocument.Load(path);
            string rawValue = doc.Root?
                .Element("AccessMode")?
                .Attribute("value")?
                .Value;

            if (!TryParse(rawValue, out loadedMode))
            {
                loadedMode = AccessMode.Personal;
                Debug.LogWarning($"[VisitedTraderTeleport] Invalid AccessMode '{rawValue}', using Personal.");
            }

            loadedTravelCost = ParseTravelCost(doc.Root?.Element("TravelCost"));
            loadedTravelTransition = ParseTravelTransition(doc.Root?.Element("TravelTransition"));
            Debug.Log(
                $"[VisitedTraderTeleport] Loaded config from '{path}': " +
                $"accessMode={loadedMode}, " +
                $"travelCostEnabled={loadedTravelCost.Enabled}, item={loadedTravelCost.ItemName}, " +
                $"perMeter={loadedTravelCost.PerMeter:0.####}, minimum={loadedTravelCost.Minimum}, " +
                $"transitionEnabled={loadedTravelTransition.Enabled}, " +
                $"duration={loadedTravelTransition.DurationSeconds:0.##}, " +
                $"sound={loadedTravelTransition.Sound}, " +
                $"soundRepeatSeconds={loadedTravelTransition.SoundRepeatSeconds:0.##}.");
        }
        catch (Exception ex)
        {
            loadedMode = AccessMode.Personal;
            loadedTravelCost = TravelCostSettings.Disabled();
            loadedTravelTransition = TravelTransitionSettings.Default();
            Debug.LogWarning($"[VisitedTraderTeleport] Could not read config, using Personal: {ex.Message}");
        }
    }

    private static TravelCostSettings ParseTravelCost(XElement element)
    {
        var settings = TravelCostSettings.Disabled();
        if (element == null)
        {
            return settings;
        }

        settings.Enabled = TryParseBool(GetAttributeValue(element, "enabled"));
        settings.ItemName = GetStringAttribute(element, "item", settings.ItemName);
        settings.ItemDisplayName = GetStringAttribute(element, "displayName", settings.ItemDisplayName);
        float perMeter = GetFloatAttribute(element, "perMeter", GetLegacyPerMeter(element, settings.PerMeter));
        if (settings.Enabled && !(perMeter > 0f))
        {
            Debug.LogWarning("[VisitedTraderTeleport] TravelCost perMeter must be greater than 0 when travel costs are enabled; using 0.1.");
            perMeter = 0.1f;
        }

        settings.PerMeter = ClampFloatAttribute("TravelCost perMeter", perMeter, 0f, MaxTravelCostPerMeter);
        settings.Minimum = ClampIntAttribute("TravelCost minimum", GetIntAttribute(element, "minimum", settings.Minimum), 0, MaxTravelCostMinimum);
        return settings;
    }

    private static float GetLegacyPerMeter(XElement element, float fallback)
    {
        string value = GetAttributeValue(element, "perKilometer");
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? parsed / 1000f
            : fallback;
    }

    private static TravelTransitionSettings ParseTravelTransition(XElement element)
    {
        var settings = TravelTransitionSettings.Default();
        if (element == null)
        {
            return settings;
        }

        string rawEnabled = GetAttributeValue(element, "enabled");
        if (!string.IsNullOrWhiteSpace(rawEnabled))
        {
            settings.Enabled = TryParseBool(rawEnabled);
        }

        settings.DurationSeconds = ClampFloatAttribute(
            "TravelTransition durationSeconds",
            GetFloatAttribute(element, "durationSeconds", settings.DurationSeconds),
            0f,
            MaxTravelTransitionDurationSeconds);
        settings.Sound = GetStringAttribute(element, "sound", settings.Sound);
        settings.SoundRepeatSeconds = ClampFloatAttribute(
            "TravelTransition soundRepeatSeconds",
            GetFloatAttribute(element, "soundRepeatSeconds", settings.SoundRepeatSeconds),
            0f,
            MaxTravelSoundRepeatSeconds);
        return settings;
    }

    private static float ClampFloatAttribute(string label, float value, float min, float max)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Invalid {label}; using {min:0.####}.");
            return min;
        }

        float clamped = Math.Max(min, Math.Min(max, value));
        if (Math.Abs(clamped - value) > 0.0001f)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] {label} was outside {min:0.####}-{max:0.####}; using {clamped:0.####}.");
        }

        return clamped;
    }

    private static int ClampIntAttribute(string label, int value, int min, int max)
    {
        int clamped = Math.Max(min, Math.Min(max, value));
        if (clamped != value)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] {label} was outside {min}-{max}; using {clamped}.");
        }

        return clamped;
    }

    private static string GetAttributeValue(XElement element, string name)
    {
        return element?
            .Attribute(name)?
            .Value;
    }

    private static string GetStringAttribute(XElement element, string name, string fallback)
    {
        string value = GetAttributeValue(element, name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static int GetIntAttribute(XElement element, string name, int fallback)
    {
        string value = GetAttributeValue(element, name);
        return int.TryParse(value, out int parsed) ? parsed : fallback;
    }

    private static float GetFloatAttribute(XElement element, string name, float fallback)
    {
        string value = GetAttributeValue(element, name);
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : fallback;
    }

    private static bool TryParseBool(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "true":
            case "1":
            case "yes":
            case "on":
                return true;
            default:
                return false;
        }
    }

    private static bool TryParse(string value, out AccessMode mode)
    {
        mode = AccessMode.Personal;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "personal":
                mode = AccessMode.Personal;
                return true;
            case "party":
                mode = AccessMode.Party;
                return true;
            case "shared":
                mode = AccessMode.Shared;
                return true;
            default:
                return false;
        }
    }

    private static string GetConfigPath()
    {
        if (!string.IsNullOrEmpty(modPath))
        {
            string modConfigPath = Path.Combine(modPath, "Config", ConfigFileName);
            if (File.Exists(modConfigPath))
            {
                return modConfigPath;
            }
        }

        string assemblyPath = Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrEmpty(assemblyPath))
        {
            string assemblyDirectory = Path.GetDirectoryName(assemblyPath);
            if (!string.IsNullOrEmpty(assemblyDirectory))
            {
                string installedPath = Path.Combine(assemblyDirectory, "Config", ConfigFileName);
                if (File.Exists(installedPath))
                {
                    return installedPath;
                }
            }
        }

        return Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Mods",
            "VisitedTraderTeleport",
            "Config",
            ConfigFileName);
    }
}
