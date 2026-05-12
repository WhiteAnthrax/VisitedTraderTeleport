namespace VisitedTraderTeleport;

public sealed class NetPackageVisitedTraderSnapshotRequest : NetPackage
{
    public override NetPackageDirection PackageDirection => NetPackageDirection.ToServer;

    internal NetPackageVisitedTraderSnapshotRequest Setup()
    {
        return this;
    }

    public override void read(PooledBinaryReader reader)
    {
    }

    public override void write(PooledBinaryWriter writer)
    {
        base.write(writer);
    }

    public override int GetLength()
    {
        return 0;
    }

    public override void ProcessPackage(World world, GameManager callbacks)
    {
        VisitedTraderNetwork.SendSnapshot(Sender);
    }
}
