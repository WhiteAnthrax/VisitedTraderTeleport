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

                    // The heavy destination observer used to start here, before the server
                    // had approved the trip, so a refused request (cooldown, busy transport)
                    // still ran up to 12 seconds of mesh work for nothing. It now starts when
                    // the server's approval package arrives, keyed by the destination that
                    // package carries.
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
