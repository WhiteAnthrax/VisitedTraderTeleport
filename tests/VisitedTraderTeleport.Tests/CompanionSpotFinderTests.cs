using System;
using System.Collections.Generic;
using System.Linq;
using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class CompanionSpotFinderTests
{
    [Fact]
    public void GetCandidateOffsets_TotalZeroOrLess_UsesZeroAngle()
    {
        List<Position3> offsets = CompanionSpotFinder.GetCandidateOffsets(0, 0).ToList();

        Assert.Equal(1.8f, offsets[0].X, 3);
        Assert.Equal(0f, offsets[0].Z, 3);
    }

    [Fact]
    public void GetCandidateOffsets_ReturnsThreeCandidatesInDescendingRadiusOrder()
    {
        List<Position3> offsets = CompanionSpotFinder.GetCandidateOffsets(0, 1).ToList();

        Assert.Equal(3, offsets.Count);
        Assert.Equal(1.8f, Magnitude(offsets[0]), 3);
        Assert.Equal(1.2f, Magnitude(offsets[1]), 3);
        Assert.Equal(0.7f, Magnitude(offsets[2]), 3);
    }

    [Fact]
    public void GetCandidateOffsets_QuarterTurn_RotatesToPositiveZAxis()
    {
        List<Position3> offsets = CompanionSpotFinder.GetCandidateOffsets(1, 4).ToList();

        Assert.Equal(0f, offsets[0].X, 3);
        Assert.Equal(1.8f, offsets[0].Z, 3);
    }

    [Fact]
    public void GetCandidateOffsets_AllOffsetsHaveZeroYComponent()
    {
        foreach (Position3 offset in CompanionSpotFinder.GetCandidateOffsets(2, 5))
        {
            Assert.Equal(0f, offset.Y);
        }
    }

    private static float Magnitude(Position3 p) => MathF.Sqrt(p.X * p.X + p.Z * p.Z);
}
