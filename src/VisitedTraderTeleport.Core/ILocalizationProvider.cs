namespace VisitedTraderTeleport;

internal interface ILocalizationProvider
{
    string Get(string key);

    string Format(string key, params object[] args);
}
