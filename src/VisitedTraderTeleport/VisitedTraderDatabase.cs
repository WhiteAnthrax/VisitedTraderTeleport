using System.Collections.Generic;

namespace VisitedTraderTeleport;

internal sealed class VisitedTraderDatabase
{
    public int SchemaVersion = 1;
    public Dictionary<string, TraderDestinationRecord> Traders = new();
    public Dictionary<string, HashSet<string>> VisitsByPlayer = new();
}

internal sealed class TraderDestinationRecord
{
    public string Key;
    public string DisplayName;
    public float PositionX;
    public float PositionY;
    public float PositionZ;
    public float ForwardX;
    public float ForwardY;
    public float ForwardZ;
    public int AreaX;
    public int AreaZ;
}
