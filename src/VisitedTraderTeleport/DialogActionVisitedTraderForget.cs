namespace VisitedTraderTeleport;

// Forgets the destination the player was looking at.
//
// Single player and a listen server's host do it here; a connected client asks the server,
// because the client holds a snapshot rather than the database. Same split as travelling.
internal sealed class DialogActionVisitedTraderForget : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        string key = Value;
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if (VisitedTraderNetwork.IsClientOnly)
        {
            // The answer comes back as a tooltip and a fresh snapshot; there is nothing
            // sensible to say here in the meantime, and guessing would sometimes be wrong.
            VisitedTraderNetwork.RequestForget(key);
            return;
        }

        ForgetOutcome outcome = VisitedTraderStore.Forget(key, player);
        VisitedTraderTeleportService.ShowTooltip(
            player, VTTLocalization.Get(VisitForgetting.GetMessageKey(outcome)));
    }
}
