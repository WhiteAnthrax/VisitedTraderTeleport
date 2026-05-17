using System.Collections.Generic;

namespace VisitedTraderTeleport;

internal static class DialogSessionStore
{
    private static readonly Dictionary<Dialog, EntityPlayer> PlayersByDialog = new();
    private static readonly Dictionary<Dialog, TraderDestination> CurrentTradersByDialog = new();

    public static void Set(Dialog dialog, EntityPlayer player, TraderDestination currentTrader)
    {
        if (dialog == null || player == null)
        {
            return;
        }

        PlayersByDialog[dialog] = player;
        if (currentTrader != null)
        {
            CurrentTradersByDialog[dialog] = currentTrader;
        }
        else
        {
            CurrentTradersByDialog.Remove(dialog);
        }
    }

    public static void SetPlayer(Dialog dialog, EntityPlayer player)
    {
        Set(dialog, player, null);
    }

    public static EntityPlayer GetPlayer(Dialog dialog)
    {
        if (dialog != null && PlayersByDialog.TryGetValue(dialog, out EntityPlayer player))
        {
            return player;
        }

        return GameManager.Instance?.World?.GetPrimaryPlayer();
    }

    public static TraderDestination GetCurrentTrader(Dialog dialog)
    {
        return dialog != null && CurrentTradersByDialog.TryGetValue(dialog, out TraderDestination trader)
            ? trader
            : null;
    }

    public static void Remove(Dialog dialog)
    {
        if (dialog != null)
        {
            PlayersByDialog.Remove(dialog);
            CurrentTradersByDialog.Remove(dialog);
        }
    }
}
