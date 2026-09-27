using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace VisitedTraderTeleport;

// Where a player's destination marks live on their own machine, and how they are found
// again after the game moves things around.
//
// Three places were considered and rejected:
//
//   - the mod's own install folder, which is deleted and replaced on every update;
//   - the game's SavesLocal/<guid>/ folder, which belongs to the game - it has an
//     archiving mechanism (archived.flag, SaveDataLimit) and may tidy it away;
//   - a single global file, which breaks on fixed maps: two servers running Navezgane
//     produce the same trader keys at the same coordinates, so marks would bleed between
//     them.
//
// So: our own folder under the user's game data, one file per world, named by the world's
// GUID.
internal static class ClientDestinationMarkStore
{
    private const string FolderName = "VisitedTraderTeleport";
    private const string MarksFolderName = "DestinationMarks";

    private static DestinationMarks loaded;
    private static string loadedWorldId;
    private static bool loadedIsWritable;

    public static DestinationMark Get(TraderDestination destination)
    {
        return EnsureLoaded()?.Get(destination) ?? DestinationMark.None;
    }

    public static void Set(TraderDestination destination, DestinationMark mark)
    {
        DestinationMarks marks = EnsureLoaded();
        if (marks == null || !marks.Set(destination, mark))
        {
            return;
        }

        if (!loadedIsWritable)
        {
            Debug.LogWarning(
                "[VisitedTraderTeleport] Not saving destination marks: the marks file was written by a newer " +
                "version of this mod and rewriting it would discard what that version stored.");
            return;
        }

        Save(marks);
    }

    // Called when a world is left, so the next world does not inherit this one's marks.
    public static void Reset()
    {
        loaded = null;
        loadedWorldId = null;
        loadedIsWritable = false;
    }

    private static DestinationMarks EnsureLoaded()
    {
        string worldId = ResolveWorldId();
        if (string.IsNullOrEmpty(worldId))
        {
            return null;
        }

        if (loaded != null && string.Equals(worldId, loadedWorldId, StringComparison.Ordinal))
        {
            return loaded;
        }

        loadedWorldId = worldId;
        loadedIsWritable = true;
        loaded = new DestinationMarks();

        foreach (string candidate in GetReadCandidates(worldId))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                var file = JsonConvert.DeserializeObject<DestinationMarkFile>(File.ReadAllText(candidate));
                DestinationMarkReadResult result = DestinationMarkFileCodec.FromFile(file);
                loaded = result.Marks;
                loadedIsWritable = result.SafeToOverwrite;

                // Found in an older location: copy it forward, and leave the original
                // where it is. Deleting it would break a downgrade, which is the mistake
                // the legacy TXT migration in #70 exists to avoid repeating.
                if (result.SafeToOverwrite && !string.Equals(candidate, GetCurrentPath(worldId), StringComparison.Ordinal))
                {
                    Save(loaded);
                }

                break;
            }
            catch (Exception ex)
            {
                // A file we cannot read is not a file we may overwrite.
                loadedIsWritable = false;
                Debug.LogWarning($"[VisitedTraderTeleport] Could not read destination marks from {candidate}: {ex.Message}");
                break;
            }
        }

        return loaded;
    }

    private static void Save(DestinationMarks marks)
    {
        try
        {
            string path = GetCurrentPath(loadedWorldId);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            DestinationMarkFile file = DestinationMarkFileCodec.ToFile(loadedWorldId, marks);
            File.WriteAllText(path, JsonConvert.SerializeObject(file, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not save destination marks: {ex.Message}");
        }
    }

    // One resolver, so no caller ever builds this path itself. Reads walk the candidates
    // in order and copy forward; writes always go to the current one. When this list grows
    // a second entry, moving the file costs nothing.
    private static string GetCurrentPath(string worldId)
    {
        return Path.Combine(GetMarksDirectory(GameIO.GetUserGameDataDir()), worldId + ".json");
    }

    private static IEnumerable<string> GetReadCandidates(string worldId)
    {
        var roots = new List<string>();

        void AddRoot(Func<string> resolve)
        {
            try
            {
                string root = resolve();
                if (!string.IsNullOrEmpty(root) && !roots.Contains(root, StringComparer.Ordinal))
                {
                    roots.Add(root);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VisitedTraderTeleport] Could not resolve a user data directory: {ex.Message}");
            }
        }

        // The default root first - that is where writes go. The other two cover a player
        // who switched their save storage between roaming and device-local after marking
        // something; the file is still theirs, so it gets copied forward rather than lost.
        AddRoot(GameIO.GetUserGameDataDir);
        AddRoot(GameIO.GetRoamingUserGameDataDir);
        AddRoot(GameIO.GetLocalUserGameDataDir);

        return roots.Select(root => Path.Combine(GetMarksDirectory(root), worldId + ".json"));
    }

    private static string GetMarksDirectory(string userGameDataDir)
    {
        return Path.Combine(Path.Combine(userGameDataDir, FolderName), MarksFolderName);
    }

    // The world's GUID, which is what tells two servers apart even when they run the same
    // fixed map. The game sets GameGuidClient only on a client that received WorldInfo
    // from a server, so a host has to read the same value off its own world.
    private static string ResolveWorldId()
    {
        try
        {
            string clientGuid = GamePrefs.GetString(EnumGamePrefs.GameGuidClient);
            if (!string.IsNullOrEmpty(clientGuid))
            {
                return Sanitize(clientGuid);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not read the client world GUID: {ex.Message}");
        }

        try
        {
            string worldGuid = GameManager.Instance?.World?.Guid;
            if (!string.IsNullOrEmpty(worldGuid))
            {
                return Sanitize(worldGuid);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisitedTraderTeleport] Could not read the local world GUID: {ex.Message}");
        }

        return null;
    }

    // The GUID becomes a file name, so anything that is not clearly safe in one is
    // replaced rather than trusted.
    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
