using System.Collections.Generic;
using System.Linq;

namespace VisitedTraderTeleport;

// One stored mark: the mark itself, plus enough of the destination to find it again.
//
// The identity is stored whole rather than as a bare key because keys are not stable. The
// mod has renormalized them repeatedly (0.4.16, 0.4.17, 0.4.19, 0.4.21, 0.4.24), and the
// client is not the one that decides what a key looks like - the server sends whatever it
// currently produces. A mark filed under last version's key would simply stop matching.
internal sealed class DestinationMarkEntry
{
    public DestinationMarkEntry()
    {
    }

    public DestinationMarkEntry(TraderDestination destination, DestinationMark mark)
    {
        Destination = destination;
        Mark = mark;
    }

    public TraderDestination Destination;
    public DestinationMark Mark;
}

// The marks for one world, and the rule for matching them to the destinations the server
// currently sends.
//
// Matching reuses TraderMatching.IsSameTrader, the same rule the destination list itself
// uses to merge duplicates. That is deliberate: it tries the key first and falls back to
// name, trader area and position, so a key change follows through. It also means the marks
// cannot be fooled by a pair of traders the list does not already treat as one - anything
// close enough to confuse a mark was merged into a single destination before it ever got
// here.
internal sealed class DestinationMarks
{
    private readonly List<DestinationMarkEntry> entries;

    public DestinationMarks()
        : this(null)
    {
    }

    public DestinationMarks(IEnumerable<DestinationMarkEntry> existing)
    {
        entries = existing == null
            ? new List<DestinationMarkEntry>()
            : existing.Where(entry => entry?.Destination != null && entry.Mark != DestinationMark.None).ToList();
    }

    public IReadOnlyList<DestinationMarkEntry> Entries => entries;

    public DestinationMark Get(TraderDestination destination)
    {
        DestinationMarkEntry entry = Find(destination);
        return entry?.Mark ?? DestinationMark.None;
    }

    // Returns true when something changed, so the caller knows whether the file is worth
    // rewriting.
    public bool Set(TraderDestination destination, DestinationMark mark)
    {
        if (destination == null)
        {
            return false;
        }

        DestinationMarkEntry entry = Find(destination);

        if (mark == DestinationMark.None)
        {
            return entry != null && entries.Remove(entry);
        }

        if (entry == null)
        {
            entries.Add(new DestinationMarkEntry(destination, mark));
            return true;
        }

        // Re-file the entry under the identity the server is sending now, so a key that
        // drifted only has to be followed once.
        bool changed = entry.Mark != mark || !ReferenceEquals(entry.Destination, destination);
        entry.Destination = destination;
        entry.Mark = mark;
        return changed;
    }

    // Marks whose destination is not in the current list are kept, not pruned. A
    // destination can leave the list without being gone - the access mode changed, or the
    // player has not been back to that trader in this session - and a mark that vanished
    // with it would be a small, silent data loss every time.
    private DestinationMarkEntry Find(TraderDestination destination)
    {
        return entries.FirstOrDefault(entry => TraderMatching.IsSameTrader(entry.Destination, destination));
    }
}
