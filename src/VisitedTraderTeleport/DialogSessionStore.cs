using System.Collections.Generic;

namespace VisitedTraderTeleport;

internal static class DialogSessionStore
{
    private static readonly Dictionary<Dialog, EntityPlayer> PlayersByDialog = new();

    public static void SetPlayer(Dialog dialog, EntityPlayer player)
    {
        if (dialog == null || player == null)
        {
            return;
        }

        PlayersByDialog[dialog] = player;
    }

    public static EntityPlayer GetPlayer(Dialog dialog)
    {
        if (dialog != null && PlayersByDialog.TryGetValue(dialog, out EntityPlayer player))
        {
            return player;
        }

        return GameManager.Instance?.World?.GetPrimaryPlayer();
    }

    public static void Remove(Dialog dialog)
    {
        if (dialog != null)
        {
            PlayersByDialog.Remove(dialog);
        }
    }
}
