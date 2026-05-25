namespace VisitedTraderTeleport;

internal sealed class DialogActionVisitedTraderPage : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        if (!int.TryParse(Value, out int delta))
        {
            return;
        }

        DialogSessionStore.MoveDestinationPage(OwnerDialog, delta);
    }
}
