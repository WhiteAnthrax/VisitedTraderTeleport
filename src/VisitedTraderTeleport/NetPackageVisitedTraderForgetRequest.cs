namespace VisitedTraderTeleport;

// A connected client asking the server to forget one of *its own* visits.
//
// It has to go through the server for the same reason travel does: the client holds a snapshot
// of what it is allowed to see, not the database. The player is resolved from the sender
// rather than sent in the package, so a client can only ever remove its own record - there is
// no field here that could name somebody else.
public sealed class NetPackageVisitedTraderForgetRequest : NetPackage
{
    private string destinationKey = string.Empty;

    public override NetPackageDirection PackageDirection => NetPackageDirection.ToServer;

    internal NetPackageVisitedTraderForgetRequest Setup(string key)
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
        if (player == null)
        {
            return;
        }

        ForgetOutcome outcome = VisitedTraderStore.Forget(destinationKey, player);
        VisitedTraderTeleportService.ShowTooltip(
            player, VTTLocalization.Get(VisitForgetting.GetMessageKey(outcome)));
        // The client's list is a snapshot, so it keeps showing the destination until it is
        // told otherwise - including when the entry survives because another player visited
        // the same trader, which is exactly the case the player needs to see.
        VisitedTraderNetwork.SendSnapshot(Sender);
    }
}
