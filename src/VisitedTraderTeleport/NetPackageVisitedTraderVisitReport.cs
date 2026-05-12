namespace VisitedTraderTeleport;

public sealed class NetPackageVisitedTraderVisitReport : NetPackage
{
    private TraderVisitReport report = new();

    public override NetPackageDirection PackageDirection => NetPackageDirection.ToServer;

    internal NetPackageVisitedTraderVisitReport Setup(TraderVisitReport value)
    {
        report = value ?? new TraderVisitReport();
        return this;
    }

    public override void read(PooledBinaryReader reader)
    {
        report = new TraderVisitReport
        {
            Key = reader.ReadString(),
            DisplayName = reader.ReadString(),
            AreaX = reader.ReadInt32(),
            AreaZ = reader.ReadInt32()
        };
    }

    public override void write(PooledBinaryWriter writer)
    {
        base.write(writer);
        writer.ReadWrite(report.Key ?? string.Empty);
        writer.ReadWrite(report.DisplayName ?? string.Empty);
        writer.ReadWrite(report.AreaX);
        writer.ReadWrite(report.AreaZ);
    }

    public override int GetLength()
    {
        return 16 + (report.Key?.Length ?? 0) + (report.DisplayName?.Length ?? 0);
    }

    public override void ProcessPackage(World world, GameManager callbacks)
    {
        EntityPlayer player = VisitedTraderNetwork.ResolvePlayer(Sender);
        if (player == null)
        {
            return;
        }

        VisitedTraderStore.RecordReportedVisit(report, player);
        VisitedTraderNetwork.SendSnapshot(Sender);
    }
}
