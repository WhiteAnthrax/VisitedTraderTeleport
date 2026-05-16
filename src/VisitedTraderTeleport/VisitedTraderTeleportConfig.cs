using System;
using System.IO;
using System.Xml.Linq;
using UnityEngine;

namespace VisitedTraderTeleport;

internal static class VisitedTraderTeleportConfig
{
    private const string ConfigFileName = "VisitedTraderTeleport.xml";
    private static string loadedPath;
    private static AccessMode loadedMode = AccessMode.Personal;
    private static bool loadedTestRecordAllTradersOnVisit;

    public static AccessMode AccessMode
    {
        get
        {
            EnsureLoaded();
            return loadedMode;
        }
    }

    public static bool TestRecordAllTradersOnVisit
    {
        get
        {
            EnsureLoaded();
            return loadedTestRecordAllTradersOnVisit;
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
        loadedTestRecordAllTradersOnVisit = false;

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

            string rawTestValue = doc.Root?
                .Element("TestRecordAllTradersOnVisit")?
                .Attribute("value")?
                .Value;
            loadedTestRecordAllTradersOnVisit = TryParseBool(rawTestValue);
        }
        catch (Exception ex)
        {
            loadedMode = AccessMode.Personal;
            loadedTestRecordAllTradersOnVisit = false;
            Debug.LogWarning($"[VisitedTraderTeleport] Could not read config, using Personal: {ex.Message}");
        }
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
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods", "VisitedTraderTeleport", "Config", ConfigFileName);
    }
}
