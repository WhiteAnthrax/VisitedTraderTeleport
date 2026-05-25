namespace VisitedTraderTeleport;

internal static class TraderDialogStatusFormatter
{
    public static string FormatModeLine(AccessMode accessMode)
    {
        return VTTLocalization.Format(
            "vtt_mode_line",
            FormatModeName(accessMode),
            FormatModeDescription(accessMode));
    }

    public static string FormatModeName(AccessMode accessMode)
    {
        return VTTLocalization.Get(accessMode switch
        {
            AccessMode.Party => "vtt_mode_party_name",
            AccessMode.Shared => "vtt_mode_shared_name",
            _ => "vtt_mode_personal_name"
        });
    }

    private static string FormatModeDescription(AccessMode accessMode)
    {
        return VTTLocalization.Get(accessMode switch
        {
            AccessMode.Party => "vtt_mode_party_description",
            AccessMode.Shared => "vtt_mode_shared_description",
            _ => "vtt_mode_personal_description"
        });
    }
}
