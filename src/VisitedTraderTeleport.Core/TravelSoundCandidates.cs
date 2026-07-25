using System;
using System.Collections.Generic;

namespace VisitedTraderTeleport;

internal static class TravelSoundCandidates
{
    public static IEnumerable<string> GetCandidates(string soundName)
    {
        yield return soundName;
        if (soundName.StartsWith("[", StringComparison.Ordinal) && soundName.EndsWith("]", StringComparison.Ordinal))
        {
            yield return soundName.Substring(1, soundName.Length - 2);
        }
        else
        {
            yield return "[" + soundName + "]";
        }
    }
}
