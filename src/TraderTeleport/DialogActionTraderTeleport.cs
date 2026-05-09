namespace TraderTeleport;

internal sealed class DialogActionTraderTeleport : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        if (!VisitedTraderStore.TryGet(Value, out TraderDestination destination))
        {
            UnityEngine.Debug.LogWarning($"[TraderTeleport] Destination not found: {Value}");
            return;
        }

        TraderTeleportService.Teleport(player, destination);
    }
}
