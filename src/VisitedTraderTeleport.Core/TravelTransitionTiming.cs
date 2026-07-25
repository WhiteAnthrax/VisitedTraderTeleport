using System;

namespace VisitedTraderTeleport;

internal static class TravelTransitionTiming
{
    private const float HiddenTransitionTeleportMaxDelaySeconds = 1.5f;
    private const float TransitionArrivalLeadSeconds = 0.35f;

    public static float GetTeleportDelay(TravelTransitionSettings settings)
    {
        float duration = Math.Max(0f, settings?.DurationSeconds ?? 0f);
        if (duration <= 0f)
        {
            return 0f;
        }

        return Math.Min(HiddenTransitionTeleportMaxDelaySeconds, duration * 0.35f);
    }

    public static float GetTransitionHoldAfterTeleport(TravelTransitionSettings settings)
    {
        float duration = Math.Max(0f, settings?.DurationSeconds ?? 0f);
        float hold = duration - GetTeleportDelay(settings);
        return Math.Max(TransitionArrivalLeadSeconds, hold);
    }
}
