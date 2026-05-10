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
