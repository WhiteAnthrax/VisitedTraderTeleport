namespace VisitedTraderTeleport;

internal sealed class DialogActionVisitedTraderTeleport : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        if (VisitedTraderNetwork.IsClientOnly)
        {
            VisitedTraderNetwork.RequestTeleport(Value);
            return;
        }

        if (!VisitedTraderStore.TryGet(Value, player, out TraderDestination destination))
        {
            UnityEngine.Debug.LogWarning($"[VisitedTraderTeleport] Destination not found: {Value}");
            return;
        }

        VisitedTraderTeleportService.Teleport(player, destination);
    }
}
