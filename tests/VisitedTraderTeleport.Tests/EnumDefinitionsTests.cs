using System;
using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

// AccessMode/ConfirmationMode names are persisted in XML config and save data, so an
// accidental rename here is a silent config-breaking regression, not just a compile error.
public class EnumDefinitionsTests
{
    [Theory]
    [InlineData(nameof(AccessMode.Personal))]
    [InlineData(nameof(AccessMode.Party))]
    [InlineData(nameof(AccessMode.Shared))]
    public void AccessMode_ExpectedNameStillDefined(string name)
    {
        Assert.True(Enum.TryParse(typeof(AccessMode), name, out _));
    }

    [Theory]
    [InlineData(nameof(ConfirmationMode.Off))]
    [InlineData(nameof(ConfirmationMode.Always))]
    [InlineData(nameof(ConfirmationMode.WhenCost))]
    public void ConfirmationMode_ExpectedNameStillDefined(string name)
    {
        Assert.True(Enum.TryParse(typeof(ConfirmationMode), name, out _));
    }
}
