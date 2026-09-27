using System.Linq;
using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class DestinationMarksTests
{
    private static TraderDestination CreateDestination(
        string key, string displayName, float x, float z, int areaX = 0, int areaZ = 0)
    {
        return new TraderDestination
        {
            Key = key,
            DisplayName = displayName,
            Position = new Position3(x, 0f, z),
            Forward = Position3.Forward,
            AreaX = areaX,
            AreaZ = areaZ
        };
    }

    [Fact]
    public void Get_Unmarked_ReturnsNone()
    {
        var marks = new DestinationMarks();

        Assert.Equal(DestinationMark.None, marks.Get(CreateDestination("joel:100:200", "Joel", 100f, 200f)));
    }

    [Fact]
    public void Get_AfterSet_ReturnsTheMark()
    {
        var marks = new DestinationMarks();
        TraderDestination joel = CreateDestination("joel:100:200", "Joel", 100f, 200f);

        marks.Set(joel, DestinationMark.Red);

        Assert.Equal(DestinationMark.Red, marks.Get(joel));
    }

    [Fact]
    public void Get_DifferentTrader_StaysUnmarked()
    {
        var marks = new DestinationMarks();
        marks.Set(CreateDestination("joel:100:200", "Joel", 100f, 200f), DestinationMark.Red);

        TraderDestination elsewhere = CreateDestination("joel:900:900", "Joel", 900f, 900f, areaX: 9, areaZ: 9);

        Assert.Equal(DestinationMark.None, marks.Get(elsewhere));
    }

    // The reason the whole identity is stored rather than the key alone: the mod has
    // renormalized destination keys repeatedly, and the client does not get a say.
    [Fact]
    public void Get_KeyChangedButSameTrader_FollowsTheMark()
    {
        var marks = new DestinationMarks();
        marks.Set(CreateDestination("joel:100:200", "Joel", 100f, 200f), DestinationMark.Blue);

        TraderDestination renamedKey = CreateDestination("trader_joel@100.0,200.0", "Joel", 100f, 200f);

        Assert.Equal(DestinationMark.Blue, marks.Get(renamedKey));
    }

    [Fact]
    public void Set_KeyChangedButSameTrader_RefilesUnderTheNewKey()
    {
        var marks = new DestinationMarks();
        marks.Set(CreateDestination("joel:100:200", "Joel", 100f, 200f), DestinationMark.Blue);

        TraderDestination renamedKey = CreateDestination("trader_joel@100.0,200.0", "Joel", 100f, 200f);
        marks.Set(renamedKey, DestinationMark.Blue);

        Assert.Single(marks.Entries);
        Assert.Equal("trader_joel@100.0,200.0", marks.Entries[0].Destination.Key);
    }

    [Fact]
    public void Set_SameTraderTwice_ReplacesRatherThanDuplicates()
    {
        var marks = new DestinationMarks();
        TraderDestination joel = CreateDestination("joel:100:200", "Joel", 100f, 200f);

        marks.Set(joel, DestinationMark.Red);
        marks.Set(joel, DestinationMark.Green);

        Assert.Single(marks.Entries);
        Assert.Equal(DestinationMark.Green, marks.Get(joel));
    }

    [Fact]
    public void Set_None_RemovesTheMark()
    {
        var marks = new DestinationMarks();
        TraderDestination joel = CreateDestination("joel:100:200", "Joel", 100f, 200f);
        marks.Set(joel, DestinationMark.Red);

        Assert.True(marks.Set(joel, DestinationMark.None));

        Assert.Empty(marks.Entries);
        Assert.Equal(DestinationMark.None, marks.Get(joel));
    }

    [Fact]
    public void Set_NoneOnUnmarked_ReportsNoChange()
    {
        var marks = new DestinationMarks();

        Assert.False(marks.Set(CreateDestination("joel:100:200", "Joel", 100f, 200f), DestinationMark.None));
    }

    [Fact]
    public void Set_SameMarkAgain_ReportsNoChange()
    {
        var marks = new DestinationMarks();
        TraderDestination joel = CreateDestination("joel:100:200", "Joel", 100f, 200f);
        marks.Set(joel, DestinationMark.Red);

        Assert.False(marks.Set(joel, DestinationMark.Red));
    }

    [Fact]
    public void Set_NullDestination_ReportsNoChange()
    {
        var marks = new DestinationMarks();

        Assert.False(marks.Set(null, DestinationMark.Red));
    }

    // A destination can leave the list without being gone - the access mode changed, or
    // the player has not been back to that trader this session. Dropping its mark would be
    // a small silent loss every time that happened.
    [Fact]
    public void Entries_DestinationNotCurrentlyListed_KeepsTheMark()
    {
        var stored = new[]
        {
            new DestinationMarkEntry(CreateDestination("hugh:500:600", "Hugh", 500f, 600f), DestinationMark.Yellow)
        };

        var marks = new DestinationMarks(stored);
        marks.Set(CreateDestination("joel:100:200", "Joel", 100f, 200f), DestinationMark.Red);

        Assert.Equal(2, marks.Entries.Count);
        Assert.Contains(marks.Entries, entry => entry.Mark == DestinationMark.Yellow);
    }

    [Fact]
    public void Constructor_DiscardsUnusableStoredEntries()
    {
        var stored = new[]
        {
            new DestinationMarkEntry(CreateDestination("joel:100:200", "Joel", 100f, 200f), DestinationMark.Red),
            new DestinationMarkEntry(null, DestinationMark.Blue),
            new DestinationMarkEntry(CreateDestination("hugh:500:600", "Hugh", 500f, 600f), DestinationMark.None)
        };

        var marks = new DestinationMarks(stored);

        Assert.Single(marks.Entries);
        Assert.Equal(DestinationMark.Red, marks.Entries.Single().Mark);
    }

    [Fact]
    public void Constructor_NullInput_StartsEmpty()
    {
        Assert.Empty(new DestinationMarks(null).Entries);
    }
}
