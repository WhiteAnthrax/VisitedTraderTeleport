namespace VisitedTraderTeleport;

public sealed class NetPackageVisitedTraderTravelTransition : NetPackage
{
    private string destinationName = string.Empty;
    private int cost;
    private TravelTransitionSettings settings = TravelTransitionSettings.Default();

    public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;

    internal NetPackageVisitedTraderTravelTransition Setup(string name, int paidCost, TravelTransitionSettings transitionSettings)
    {
        destinationName = name ?? string.Empty;
        cost = paidCost;
        settings = transitionSettings?.Clone() ?? TravelTransitionSettings.Default();
        return this;
    }

    public override void read(PooledBinaryReader reader)
    {
        destinationName = reader.ReadString();
        cost = reader.ReadInt32();
        settings = new TravelTransitionSettings
        {
            Enabled = reader.ReadBoolean(),
            DurationSeconds = reader.ReadSingle(),
            DisableCamera = reader.ReadBoolean(),
            Sound = reader.ReadString(),
            SoundRepeatSeconds = reader.ReadSingle()
        };
    }

    public override void write(PooledBinaryWriter writer)
    {
        base.write(writer);
        writer.ReadWrite(destinationName ?? string.Empty);
        writer.ReadWrite(cost);
        writer.ReadWrite(settings.Enabled);
        writer.ReadWrite(settings.DurationSeconds);
        writer.ReadWrite(settings.DisableCamera);
        writer.ReadWrite(settings.Sound ?? string.Empty);
        writer.ReadWrite(settings.SoundRepeatSeconds);
    }

    public override int GetLength()
    {
        return 28 +
               (destinationName?.Length ?? 0) +
               (settings.Sound?.Length ?? 0);
    }

    public override void ProcessPackage(World world, GameManager callbacks)
    {
        EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
        VisitedTraderTeleportService.PlayClientTravelTransition(player, destinationName, cost, settings);
    }
}
