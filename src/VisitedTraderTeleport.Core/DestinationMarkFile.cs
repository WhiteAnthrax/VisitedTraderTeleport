using System;
using System.Collections.Generic;

namespace VisitedTraderTeleport;

// The on-disk shape of one world's marks. Plain fields, so the mod side only has to hand
// it to the JSON serializer it already uses for the visit database.
internal sealed class DestinationMarkFile
{
    public int SchemaVersion = DestinationMarkFileCodec.CurrentSchemaVersion;

    // Also in the file name. Keeping it in the content too means the naming scheme can
    // change later without the files becoming anonymous - they can be re-filed by reading
    // them.
    public string WorldId;

    public List<DestinationMarkFileEntry> Marks = new List<DestinationMarkFileEntry>();
}

internal sealed class DestinationMarkFileEntry
{
    // The enum name rather than its number, so the file stays readable and a reordered
    // enum cannot silently repaint every mark.
    public string Mark;

    public TraderDestinationRecord Destination;
}

internal sealed class DestinationMarkReadResult
{
    public DestinationMarkReadResult(DestinationMarks marks, bool safeToOverwrite)
    {
        Marks = marks;
        SafeToOverwrite = safeToOverwrite;
    }

    public DestinationMarks Marks;

    // False when the file holds something this build does not understand - a newer schema,
    // or a mark name that does not exist here. Rewriting it would throw away whatever a
    // later version wrote, so the caller reads it and leaves it alone.
    public bool SafeToOverwrite;
}

internal static class DestinationMarkFileCodec
{
    public const int CurrentSchemaVersion = 1;

    public static DestinationMarkFile ToFile(string worldId, DestinationMarks marks)
    {
        var file = new DestinationMarkFile
        {
            SchemaVersion = CurrentSchemaVersion,
            WorldId = worldId
        };

        if (marks == null)
        {
            return file;
        }

        foreach (DestinationMarkEntry entry in marks.Entries)
        {
            file.Marks.Add(new DestinationMarkFileEntry
            {
                Mark = entry.Mark.ToString(),
                Destination = TraderRecordConverter.ToRecord(entry.Destination)
            });
        }

        return file;
    }

    public static DestinationMarkReadResult FromFile(DestinationMarkFile file)
    {
        if (file == null)
        {
            return new DestinationMarkReadResult(new DestinationMarks(), safeToOverwrite: true);
        }

        // A newer build wrote this. Read what is recognisable and never write back.
        bool safeToOverwrite = file.SchemaVersion <= CurrentSchemaVersion;

        var entries = new List<DestinationMarkEntry>();
        foreach (DestinationMarkFileEntry entry in file.Marks ?? new List<DestinationMarkFileEntry>())
        {
            if (entry?.Destination == null)
            {
                continue;
            }

            if (!Enum.TryParse(entry.Mark, out DestinationMark mark) || !Enum.IsDefined(typeof(DestinationMark), mark))
            {
                // A colour this build does not have. Same reasoning as a newer schema:
                // keep the file intact rather than dropping someone else's mark.
                safeToOverwrite = false;
                continue;
            }

            if (mark == DestinationMark.None)
            {
                continue;
            }

            TraderDestination destination = TraderRecordConverter.FromRecord(entry.Destination, entry.Destination.Key);
            if (destination == null)
            {
                continue;
            }

            entries.Add(new DestinationMarkEntry(destination, mark));
        }

        return new DestinationMarkReadResult(new DestinationMarks(entries), safeToOverwrite);
    }
}
