using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class VisitedTraderDatabaseTests
{
    [Fact]
    public void DefaultConstructed_HasEmptyNonNullCollections()
    {
        var database = new VisitedTraderDatabase();

        Assert.NotNull(database.Traders);
        Assert.Empty(database.Traders);
        Assert.NotNull(database.VisitsByPlayer);
        Assert.Empty(database.VisitsByPlayer);
    }

    [Fact]
    public void DefaultConstructed_HasSchemaVersionOne()
    {
        var database = new VisitedTraderDatabase();

        Assert.Equal(1, database.SchemaVersion);
    }
}
