using System;

namespace VisitedTraderTeleport;

internal static class TravelCooldown
{
    private const float TravelCooldownSeconds = 10f;
    private const float TravelSlotMaxHoldSeconds = 60f;

    public static float GetRemainingSeconds(float currentTime, float lastTravelTime)
    {
        return TravelCooldownSeconds - (currentTime - lastTravelTime);
    }

    // A configured travel transition legitimately holds the pending flag for its full
    // duration, so the stuck-trip deadline has to sit above it.
    public static float GetTravelSlotMaxHoldSeconds(TravelTransitionSettings transitionSettings)
    {
        float transitionSeconds = Math.Max(0f, transitionSettings?.DurationSeconds ?? 0f);
        return TravelSlotMaxHoldSeconds + transitionSeconds;
    }
}
