namespace VisitedTraderTeleport;

internal sealed class DialogActionVisitedTraderConfirm : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        DialogSessionStore.SetPendingDestination(OwnerDialog, Value);
    }
}
