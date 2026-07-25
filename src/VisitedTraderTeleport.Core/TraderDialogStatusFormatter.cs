namespace VisitedTraderTeleport;

internal static class TraderDialogStatusFormatter
{
    public static string FormatModeLine(AccessMode accessMode, ILocalizationProvider localization)
    {
        return localization.Format(
            "vtt_mode_line",
            FormatModeName(accessMode, localization),
            FormatModeDescription(accessMode, localization));
    }

    public static string FormatModeName(AccessMode accessMode, ILocalizationProvider localization)
    {
        return localization.Get(accessMode switch
        {
            AccessMode.Party => "vtt_mode_party_name",
            AccessMode.Shared => "vtt_mode_shared_name",
            _ => "vtt_mode_personal_name"
        });
    }

    private static string FormatModeDescription(AccessMode accessMode, ILocalizationProvider localization)
    {
        return localization.Get(accessMode switch
        {
            AccessMode.Party => "vtt_mode_party_description",
            AccessMode.Shared => "vtt_mode_shared_description",
            _ => "vtt_mode_personal_description"
        });
    }
}
