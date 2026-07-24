namespace VisitedTraderTeleport;

internal static class VTTLocalization
{
    public static string Get(string key)
    {
        string value = Localization.Get(key);
        return string.IsNullOrEmpty(value) || value == key ? key : value;
    }

    public static string Format(string key, params object[] args)
    {
        return string.Format(Get(key), args);
    }
}

internal sealed class GameLocalizationProvider : ILocalizationProvider
{
    public static readonly GameLocalizationProvider Instance = new();

    public string Get(string key) => VTTLocalization.Get(key);

    public string Format(string key, params object[] args) => VTTLocalization.Format(key, args);
}
