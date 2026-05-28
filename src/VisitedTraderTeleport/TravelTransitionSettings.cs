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
