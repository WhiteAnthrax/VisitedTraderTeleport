namespace VisitedTraderTeleport;

internal static class ConfirmationService
{
    public static bool RequiresConfirmation(TraderDestination destination, EntityPlayer player)
    {
        switch (VisitedTraderTeleportConfig.Confirmation)
        {
            case ConfirmationMode.Always:
                return true;
            case ConfirmationMode.WhenCost:
                return TravelCostService.TryGetCostInfo(destination, player, out _, out _);
            default:
                return false;
        }
    }

    public static bool TryResolveDestination(string key, EntityPlayer player, out TraderDestination destination)
    {
        if (VisitedTraderNetwork.IsClientOnly)
        {
            return VisitedTraderClientState.TryGet(key, out destination);
        }

        return VisitedTraderStore.TryGet(key, player, out destination);
    }

    public static string FormatPrompt(TraderDestination destination, EntityPlayer player)
    {
        if (destination == null)
        {
            return VTTLocalization.Get("vtt_statement_confirm");
        }

        string name = TraderDestinationFormatter.FormatName(destination);
        if (TravelCostService.TryGetCostInfo(destination, player, out int cost, out string itemName))
        {
            return VTTLocalization.Format("vtt_confirm_prompt_cost", name, cost, itemName);
        }

        return VTTLocalization.Format("vtt_confirm_prompt", name);
    }
}
