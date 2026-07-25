using System;

namespace VisitedTraderTeleport;

internal readonly struct InventoryConsumptionResult
{
    public readonly int AvailableBefore;
    public readonly int Cost;
    public readonly int Removed;
    public readonly int RemainingAfter;

    public InventoryConsumptionResult(int availableBefore, int cost, int removed, int remainingAfter)
    {
        AvailableBefore = availableBefore;
        Cost = cost;
        Removed = removed;
        RemainingAfter = remainingAfter;
    }

    public int ExpectedRemaining => Math.Max(0, AvailableBefore - Cost);

    public bool UnderConsumed => RemainingAfter > ExpectedRemaining;

    public bool OverConsumed => RemainingAfter < ExpectedRemaining;
}
