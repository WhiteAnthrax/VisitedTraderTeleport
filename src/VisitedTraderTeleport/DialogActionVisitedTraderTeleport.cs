namespace VisitedTraderTeleport;

internal sealed class DialogActionVisitedTraderTeleport : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        if (!VisitedTraderStore.TryGet(Value, out TraderDestination destination))
        {
            UnityEngine.Debug.LogWarning($"[VisitedTraderTeleport] Destination not found: {Value}");
            return;
        }

        VisitedTraderTeleportService.Teleport(player, destination);
    }
}
