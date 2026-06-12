namespace VisitedTraderTeleport;

internal static class ConfirmationService
{
    public static bool RequiresConfirmation(TraderDestination destination, EntityPlayer player)
    {
        switch (GetEffectiveMode())
        {
            case ConfirmationMode.Always:
                return true;
            case ConfirmationMode.WhenCost:
                return TravelCostService.TryGetCostInfo(destination, player, out _, out _);
            default:
                return false;
        }
    }

    private static ConfirmationMode GetEffectiveMode()
    {
        return VisitedTraderNetwork.IsClientOnly
            ? VisitedTraderClientState.ServerConfirmation
            : VisitedTraderTeleportConfig.Confirmation;
    }

    public static bool TryResolveDestination(string key, EntityPlayer player, out TraderDestination destination)
    {
        if (VisitedTraderNetwork.IsClientOnly)
        {
            return VisitedTraderClientState.TryGet(key, out destination);
        }

        return VisitedTraderStore.TryGet(key, player, out destination);
    }

    public static string FormatPromptQuestion(TraderDestination destination)
    {
        if (destination == null)
        {
            return VTTLocalization.Get("vtt_statement_confirm");
        }

        string name = TraderDestinationFormatter.FormatName(destination);
        return VTTLocalization.Format("vtt_confirm_prompt", name);
    }

    // The cost goes on its own line because the dialog response width is narrow and a
    // combined "Travel to X? (Cost: ...)" line gets clipped.
    public static string FormatCostLine(TraderDestination destination, EntityPlayer player)
    {
        if (destination != null &&
            TravelCostService.TryGetCostInfo(destination, player, out int cost, out string itemName))
        {
            return VTTLocalization.Format("vtt_confirm_cost_line", cost, itemName);
        }

        return string.Empty;
    }
}
