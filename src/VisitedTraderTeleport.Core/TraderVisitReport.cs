namespace VisitedTraderTeleport;

internal sealed class TraderVisitReport
{
    public string Key;
    public string DisplayName;
    public int AreaX;
    public int AreaZ;
    public float TraderPositionX;
    public float TraderPositionY;
    public float TraderPositionZ;

    public bool HasTraderPosition =>
        TraderPositionX != 0f ||
        TraderPositionY != 0f ||
        TraderPositionZ != 0f;
}
