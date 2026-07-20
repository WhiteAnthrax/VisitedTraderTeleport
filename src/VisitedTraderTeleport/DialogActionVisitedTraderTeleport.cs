namespace VisitedTraderTeleport;

internal sealed class DialogActionVisitedTraderTeleport : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        if (VisitedTraderNetwork.IsClientOnly)
        {
            if (player is EntityPlayerLocal localPlayer)
            {
                if (VisitedTraderClientState.TryGet(Value, out TraderDestination clientDestination))
                {
                    if (!TravelCostService.HasRequiredCost(localPlayer, clientDestination))
                    {
                        return;
                    }

                    // Only record the request here. The heavy destination observer used to
                    // start immediately, so a request the server then refused (cooldown, busy
                    // mesh queue) still ran up to 12 seconds of mesh work for nothing. The
                    // observer now starts when the server's approval package arrives.
                    VisitedTraderClientState.SetPendingTravel(Value);
                }

                GameManager.ShowTooltip(localPlayer, VTTLocalization.Get("vtt_preparing_travel"), false, false, 2f);
            }

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
