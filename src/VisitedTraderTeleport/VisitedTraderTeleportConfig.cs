using System;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using UnityEngine;

namespace VisitedTraderTeleport;

internal static class VisitedTraderTeleportConfig
{
    private const string ConfigFileName = "VisitedTraderTeleport.xml";
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
                $"perKilometer={loadedTravelCost.PerKilometer}, minimum={loadedTravelCost.Minimum}, " +
                $"transitionEnabled={loadedTravelTransition.Enabled}, " +
                $"duration={loadedTravelTransition.DurationSeconds:0.##}, " +
                $"disableCamera={loadedTravelTransition.DisableCamera}, sound={loadedTravelTransition.Sound}.");
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
        settings.PerKilometer = Math.Max(0, GetIntAttribute(element, "perKilometer", settings.PerKilometer));
        settings.Minimum = Math.Max(0, GetIntAttribute(element, "minimum", settings.Minimum));
        return settings;
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

        settings.DurationSeconds = Math.Max(0f, GetFloatAttribute(element, "durationSeconds", settings.DurationSeconds));
        string rawDisableCamera = GetAttributeValue(element, "disableCamera");
        if (!string.IsNullOrWhiteSpace(rawDisableCamera))
        {
            settings.DisableCamera = TryParseBool(rawDisableCamera);
        }

        settings.Sound = GetStringAttribute(element, "sound", settings.Sound);
        return settings;
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
        return float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed)
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
