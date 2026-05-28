namespace VisitedTraderTeleport;

internal sealed class TravelCostSettings
{
    public bool Enabled { get; set; }
    public string ItemName { get; set; } = "ammoGasCan";
    public string ItemDisplayName { get; set; } = string.Empty;
    public float PerMeter { get; set; } = 0.1f;
    public int Minimum { get; set; }

    public static TravelCostSettings Disabled()
    {
        return new TravelCostSettings();
    }

    public TravelCostSettings Clone()
    {
        return new TravelCostSettings
        {
            Enabled = Enabled,
            ItemName = ItemName,
            ItemDisplayName = ItemDisplayName,
            PerMeter = PerMeter,
            Minimum = Minimum
        };
    }
}
