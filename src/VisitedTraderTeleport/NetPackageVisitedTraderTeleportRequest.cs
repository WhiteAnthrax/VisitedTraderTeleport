namespace VisitedTraderTeleport;

public sealed class NetPackageVisitedTraderTeleportRequest : NetPackage
{
    private string destinationKey = string.Empty;

    public override NetPackageDirection PackageDirection => NetPackageDirection.ToServer;

    internal NetPackageVisitedTraderTeleportRequest Setup(string key)
    {
        destinationKey = key ?? string.Empty;
        return this;
    }

    public override void read(PooledBinaryReader reader)
    {
        destinationKey = reader.ReadString();
    }

    public override void write(PooledBinaryWriter writer)
    {
        base.write(writer);
        writer.ReadWrite(destinationKey ?? string.Empty);
    }

    public override int GetLength()
    {
        return 4 + (destinationKey?.Length ?? 0);
    }

    public override void ProcessPackage(World world, GameManager callbacks)
    {
        EntityPlayer player = VisitedTraderNetwork.ResolvePlayer(Sender);
        if (player == null ||
            !VisitedTraderStore.TryGet(destinationKey, player, out TraderDestination destination))
        {
            return;
        }

        VisitedTraderTeleportService.Teleport(player, destination);
        VisitedTraderNetwork.SendSnapshot(Sender);
    }
}
