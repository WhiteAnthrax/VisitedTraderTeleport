using System;
using System.Collections.Generic;

namespace VisitedTraderTeleport;

internal static class CompanionSpotFinder
{
    // Widest radius first, so the caller's first unblocked candidate keeps companions spread out.
    private static readonly float[] CandidateRadii = { 1.8f, 1.2f, 0.7f };

    public static IEnumerable<Position3> GetCandidateOffsets(int index, int total)
    {
        float angle = total <= 0 ? 0f : (index / (float)total) * MathF.PI * 2f;
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        foreach (float radius in CandidateRadii)
        {
            yield return new Position3(cos * radius, 0f, sin * radius);
        }
    }
}
