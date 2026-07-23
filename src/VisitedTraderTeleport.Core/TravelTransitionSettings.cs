namespace VisitedTraderTeleport;

internal sealed class TravelTransitionSettings
{
    public bool Enabled { get; set; } = true;
    public float DurationSeconds { get; set; } = 5f;
    public string Sound { get; set; } = "suv_startup";
    public float SoundRepeatSeconds { get; set; } = 2f;

    public static TravelTransitionSettings Default()
    {
        return new TravelTransitionSettings();
    }

    public static TravelTransitionSettings Disabled()
    {
        return new TravelTransitionSettings
        {
            Enabled = false,
            DurationSeconds = 0f,
            Sound = string.Empty,
            SoundRepeatSeconds = 0f
        };
    }

    public TravelTransitionSettings Clone()
    {
        return new TravelTransitionSettings
        {
            Enabled = Enabled,
            DurationSeconds = DurationSeconds,
            Sound = Sound,
            SoundRepeatSeconds = SoundRepeatSeconds
        };
    }
}
