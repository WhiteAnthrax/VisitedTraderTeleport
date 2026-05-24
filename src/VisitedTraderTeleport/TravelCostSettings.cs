namespace VisitedTraderTeleport;

internal sealed class TravelCostSettings
{
    public bool Enabled { get; set; }
    public string ItemName { get; set; } = "ammoGasCan";
    public string ItemDisplayName { get; set; } = "gas";
    public int PerKilometer { get; set; } = 1500;
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
            PerKilometer = PerKilometer,
            Minimum = Minimum
        };
    }
}
