using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using UnityEngine;

namespace VisitedTraderTeleportTestTools;

internal enum TargetTraderMode
{
    Nearest,
    First
}

internal static class TestToolsConfig
{
    private const string ConfigFileName = "VisitedTraderTeleportTestTools.xml";
    private static string loadedPath;
    private static bool loadedTeleportToTraderOnGameStart;
    private static bool loadedRecordAllTradersOnGameStart;
    private static TargetTraderMode loadedTargetTrader = TargetTraderMode.Nearest;
    private static float loadedStartDelaySeconds = 3f;
    private static float loadedChunkLoadTimeoutSeconds = 12f;
    private static bool loadedFallbackToTraderAreaCenter = true;

    public static bool TeleportToTraderOnGameStart
    {
        get
        {
            EnsureLoaded();
            return loadedTeleportToTraderOnGameStart;
        }
    }

    public static TargetTraderMode TargetTrader
    {
        get
        {
            EnsureLoaded();
            return loadedTargetTrader;
        }
    }

    public static bool RecordAllTradersOnGameStart
    {
        get
        {
            EnsureLoaded();
            return loadedRecordAllTradersOnGameStart;
        }
    }

    public static float StartDelaySeconds
    {
        get
        {
            EnsureLoaded();
            return loadedStartDelaySeconds;
        }
    }

    public static float ChunkLoadTimeoutSeconds
    {
        get
        {
            EnsureLoaded();
            return loadedChunkLoadTimeoutSeconds;
        }
    }

    public static bool FallbackToTraderAreaCenter
    {
        get
        {
            EnsureLoaded();
            return loadedFallbackToTraderAreaCenter;
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
        loadedTeleportToTraderOnGameStart = false;
        loadedRecordAllTradersOnGameStart = false;
        loadedTargetTrader = TargetTraderMode.Nearest;
        loadedStartDelaySeconds = 3f;
        loadedChunkLoadTimeoutSeconds = 12f;
        loadedFallbackToTraderAreaCenter = true;

        try
        {
            if (!File.Exists(path))
            {
                Debug.Log("[VisitedTraderTeleportTestTools] Config not found; test tools are disabled.");
                return;
            }

            XDocument doc = XDocument.Load(path);
            loadedTeleportToTraderOnGameStart = TryParseBool(GetValue(doc, "TeleportToTraderOnGameStart"));
            loadedRecordAllTradersOnGameStart = TryParseBool(GetValue(doc, "RecordAllTradersOnGameStart"));
            loadedTargetTrader = ParseTargetTrader(GetValue(doc, "TargetTrader"));
            loadedStartDelaySeconds = ParseFloat(GetValue(doc, "StartDelaySeconds"), 3f, 0f, 60f);
            loadedChunkLoadTimeoutSeconds = ParseFloat(GetValue(doc, "ChunkLoadTimeoutSeconds"), 12f, 1f, 120f);
            loadedFallbackToTraderAreaCenter = TryParseBool(GetValue(doc, "FallbackToTraderAreaCenter"), true);
        }
        catch (Exception ex)
        {
            loadedTeleportToTraderOnGameStart = false;
            loadedRecordAllTradersOnGameStart = false;
            Debug.LogWarning($"[VisitedTraderTeleportTestTools] Could not read config; test tools are disabled: {ex.Message}");
        }
    }

    private static string GetValue(XDocument doc, string elementName)
    {
        return doc.Root?
            .Element(elementName)?
            .Attribute("value")?
            .Value;
    }

    private static TargetTraderMode ParseTargetTrader(string value)
    {
        return string.Equals(value?.Trim(), "first", StringComparison.OrdinalIgnoreCase)
            ? TargetTraderMode.First
            : TargetTraderMode.Nearest;
    }

    private static bool TryParseBool(string value, bool fallback = false)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "true":
            case "1":
            case "yes":
            case "on":
                return true;
            case "false":
            case "0":
            case "no":
            case "off":
                return false;
            default:
                return fallback;
        }
    }

    private static float ParseFloat(string value, float fallback, float min, float max)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
        {
            return fallback;
        }

        return Mathf.Clamp(parsed, min, max);
    }

    private static string GetConfigPath()
    {
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
            "VisitedTraderTeleportTestTools",
            "Config",
            ConfigFileName);
    }
}
