namespace VisitedTraderTeleport;

public sealed class NetPackageVisitedTraderTravelTransition : NetPackage
{
    private string destinationKey = string.Empty;
    private string destinationName = string.Empty;
    private string transportDestination = string.Empty;
    private int cost;
    private string costItemName = string.Empty;
    private TravelTransitionSettings settings = TravelTransitionSettings.Default();

    public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;

    internal NetPackageVisitedTraderTravelTransition Setup(
        string key,
        string name,
        string transportName,
        int paidCost,
        string itemName,
        TravelTransitionSettings transitionSettings)
    {
        destinationKey = key ?? string.Empty;
        destinationName = name ?? string.Empty;
        transportDestination = string.IsNullOrWhiteSpace(transportName) ? destinationName : transportName;
        cost = paidCost;
        costItemName = itemName ?? string.Empty;
        settings = transitionSettings?.Clone() ?? TravelTransitionSettings.Default();
        return this;
    }

    public override void read(PooledBinaryReader reader)
    {
        destinationKey = reader.ReadString();
        destinationName = reader.ReadString();
        transportDestination = reader.ReadString();
        cost = reader.ReadInt32();
        costItemName = reader.ReadString();
        settings = new TravelTransitionSettings
        {
            Enabled = reader.ReadBoolean(),
            DurationSeconds = reader.ReadSingle(),
            Sound = reader.ReadString(),
            SoundRepeatSeconds = reader.ReadSingle()
        };
    }

    public override void write(PooledBinaryWriter writer)
    {
        base.write(writer);
        writer.ReadWrite(destinationKey ?? string.Empty);
        writer.ReadWrite(destinationName ?? string.Empty);
        writer.ReadWrite(transportDestination ?? string.Empty);
        writer.ReadWrite(cost);
        writer.ReadWrite(costItemName ?? string.Empty);
        writer.ReadWrite(settings.Enabled);
        writer.ReadWrite(settings.DurationSeconds);
        writer.ReadWrite(settings.Sound ?? string.Empty);
        writer.ReadWrite(settings.SoundRepeatSeconds);
    }

    public override int GetLength()
    {
        return 35 +
               (destinationKey?.Length ?? 0) +
               (destinationName?.Length ?? 0) +
               (transportDestination?.Length ?? 0) +
               (costItemName?.Length ?? 0) +
               (settings.Sound?.Length ?? 0);
    }

    public override void ProcessPackage(World world, GameManager callbacks)
    {
        EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
        if (player != null && cost > 0 && !string.IsNullOrWhiteSpace(costItemName))
        {
            TravelCostService.TryConsumeLocalCost(player, costItemName, cost);
        }

        // This package is the server's approval of a trip this client requested, so only now
        // start pre-loading the destination visuals. The destination key it carries pins the
        // pre-load to the approved trip's destination, even if the player has since clicked a
        // different one. A refused request never receives this package, so a rejected trip no
        // longer feeds the mesh queue.
        if (player != null &&
            !string.IsNullOrEmpty(destinationKey) &&
            VisitedTraderClientState.TryGet(destinationKey, out TraderDestination approvedDestination))
        {
            VisitedTraderTeleportService.PrepareClientDestinationVisuals(player, approvedDestination);
        }

        VisitedTraderTeleportService.PlayClientTravelTransition(player, destinationName, transportDestination, cost, settings);
    }
}
