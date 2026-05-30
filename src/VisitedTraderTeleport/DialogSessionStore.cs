using System.Collections.Generic;

namespace VisitedTraderTeleport;

internal static class DialogSessionStore
{
    private static readonly Dictionary<Dialog, EntityPlayer> PlayersByDialog = new();
    private static readonly Dictionary<Dialog, TraderDestination> CurrentTradersByDialog = new();
    private static readonly Dictionary<Dialog, int> DestinationPagesByDialog = new();
    private static readonly Dictionary<Dialog, string> PendingDestinationsByDialog = new();

    public static void Set(Dialog dialog, EntityPlayer player, TraderDestination currentTrader)
    {
        if (dialog == null || player == null)
        {
            return;
        }

        PlayersByDialog[dialog] = player;
        DestinationPagesByDialog[dialog] = 0;
        PendingDestinationsByDialog.Remove(dialog);
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

    public static int GetDestinationPage(Dialog dialog)
    {
        return dialog != null && DestinationPagesByDialog.TryGetValue(dialog, out int page)
            ? page
            : 0;
    }

    public static void SetDestinationPage(Dialog dialog, int page)
    {
        if (dialog == null)
        {
            return;
        }

        DestinationPagesByDialog[dialog] = page < 0 ? 0 : page;
    }

    public static void MoveDestinationPage(Dialog dialog, int delta)
    {
        if (dialog == null || delta == 0)
        {
            return;
        }

        SetDestinationPage(dialog, GetDestinationPage(dialog) + delta);
    }

    public static void SetPendingDestination(Dialog dialog, string key)
    {
        if (dialog == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(key))
        {
            PendingDestinationsByDialog.Remove(dialog);
        }
        else
        {
            PendingDestinationsByDialog[dialog] = key;
        }
    }

    public static string GetPendingDestination(Dialog dialog)
    {
        return dialog != null && PendingDestinationsByDialog.TryGetValue(dialog, out string key)
            ? key
            : null;
    }

    public static void Remove(Dialog dialog)
    {
        if (dialog != null)
        {
            PlayersByDialog.Remove(dialog);
            CurrentTradersByDialog.Remove(dialog);
            DestinationPagesByDialog.Remove(dialog);
            PendingDestinationsByDialog.Remove(dialog);
        }
    }
}
