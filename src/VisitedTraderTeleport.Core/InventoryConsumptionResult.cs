using System;

namespace VisitedTraderTeleport;

internal readonly struct InventoryConsumptionResult
{
    public int AvailableBefore { get; init; }
    public int Cost { get; init; }
    public int Removed { get; init; }
    public int RemainingAfter { get; init; }

    public int ExpectedRemaining => Math.Max(0, AvailableBefore - Cost);

    public bool UnderConsumed => RemainingAfter > ExpectedRemaining;

    public bool OverConsumed => RemainingAfter < ExpectedRemaining;
}
