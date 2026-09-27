using System.Collections.Generic;
using System.Linq;
using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class DestinationMarkFileCodecTests
{
    private static TraderDestination CreateDestination(string key, string displayName, float x, float z)
    {
        return new TraderDestination
        {
            Key = key,
            DisplayName = displayName,
            Position = new Position3(x, 0f, z),
            Forward = Position3.Forward,
            AreaX = 1,
            AreaZ = 2,
            Biome = "forest"
        };
    }

    private static DestinationMarkFile FileWith(string markName, string key = "joel:100:200")
    {
        return new DestinationMarkFile
        {
            SchemaVersion = DestinationMarkFileCodec.CurrentSchemaVersion,
            WorldId = "world-guid",
            Marks = new List<DestinationMarkFileEntry>
            {
                new DestinationMarkFileEntry
                {
                    Mark = markName,
                    Destination = TraderRecordConverter.ToRecord(CreateDestination(key, "Joel", 100f, 200f))
                }
            }
        };
    }

    [Fact]
    public void RoundTrip_PreservesMarkAndDestination()
    {
        var marks = new DestinationMarks();
        TraderDestination joel = CreateDestination("joel:100:200", "Joel", 100f, 200f);
        marks.Set(joel, DestinationMark.Green);

        DestinationMarkFile file = DestinationMarkFileCodec.ToFile("world-guid", marks);
        DestinationMarkReadResult result = DestinationMarkFileCodec.FromFile(file);

        Assert.Equal("world-guid", file.WorldId);
        Assert.Equal(DestinationMark.Green, result.Marks.Get(joel));
        Assert.True(result.SafeToOverwrite);
    }

    // The name, not the number: a reordered enum must not repaint saved marks.
    [Fact]
    public void ToFile_WritesTheMarkName()
    {
        var marks = new DestinationMarks();
        marks.Set(CreateDestination("joel:100:200", "Joel", 100f, 200f), DestinationMark.Yellow);

        DestinationMarkFile file = DestinationMarkFileCodec.ToFile("world-guid", marks);

        Assert.Equal(nameof(DestinationMark.Yellow), file.Marks.Single().Mark);
    }

    [Fact]
    public void FromFile_Null_IsEmptyAndWritable()
    {
        DestinationMarkReadResult result = DestinationMarkFileCodec.FromFile(null);

        Assert.Empty(result.Marks.Entries);
        Assert.True(result.SafeToOverwrite);
    }

    // Written by a later version. Read what we can; never write back over it.
    [Fact]
    public void FromFile_NewerSchema_IsNotSafeToOverwrite()
    {
        DestinationMarkFile file = FileWith(nameof(DestinationMark.Red));
        file.SchemaVersion = DestinationMarkFileCodec.CurrentSchemaVersion + 1;

        DestinationMarkReadResult result = DestinationMarkFileCodec.FromFile(file);

        Assert.False(result.SafeToOverwrite);
        Assert.Equal(DestinationMark.Red, result.Marks.Get(CreateDestination("joel:100:200", "Joel", 100f, 200f)));
    }

    [Fact]
    public void FromFile_UnknownMarkName_IsNotSafeToOverwrite()
    {
        DestinationMarkReadResult result = DestinationMarkFileCodec.FromFile(FileWith("Purple"));

        Assert.False(result.SafeToOverwrite);
        Assert.Empty(result.Marks.Entries);
    }

    [Fact]
    public void FromFile_EntryWithoutDestination_IsSkipped()
    {
        var file = new DestinationMarkFile
        {
            Marks = new List<DestinationMarkFileEntry>
            {
                new DestinationMarkFileEntry { Mark = nameof(DestinationMark.Red), Destination = null }
            }
        };

        DestinationMarkReadResult result = DestinationMarkFileCodec.FromFile(file);

        Assert.Empty(result.Marks.Entries);
        Assert.True(result.SafeToOverwrite);
    }

    [Fact]
    public void FromFile_NoneMark_IsDropped()
    {
        DestinationMarkReadResult result = DestinationMarkFileCodec.FromFile(FileWith(nameof(DestinationMark.None)));

        Assert.Empty(result.Marks.Entries);
        Assert.True(result.SafeToOverwrite);
    }

    [Fact]
    public void FromFile_MissingMarkList_IsEmptyAndWritable()
    {
        var file = new DestinationMarkFile { Marks = null };

        DestinationMarkReadResult result = DestinationMarkFileCodec.FromFile(file);

        Assert.Empty(result.Marks.Entries);
        Assert.True(result.SafeToOverwrite);
    }
}
