namespace VisitedTraderTeleport;

// localX/localZ are the position's offset from the trader area's origin, already quantized
// to the game's bucket size - the quantization itself uses the game's own rounding (Mathf),
// so it stays on the implementation side rather than being redone here.
internal interface ITraderAreaLookup
{
    bool TryFindTraderArea(Position3 position, out int areaX, out int areaZ, out int localX, out int localZ);
}
