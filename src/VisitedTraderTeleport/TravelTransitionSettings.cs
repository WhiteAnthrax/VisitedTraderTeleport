namespace VisitedTraderTeleport;

internal sealed class TravelTransitionSettings
{
    public bool Enabled { get; set; } = true;
    public float DurationSeconds { get; set; } = 5f;
    public bool DisableCamera { get; set; } = false;
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
            DisableCamera = DisableCamera,
            Sound = Sound,
            SoundRepeatSeconds = SoundRepeatSeconds
        };
    }
}
