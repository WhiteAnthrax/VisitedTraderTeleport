using System;
using System.Collections.Generic;
using System.Linq;

namespace VisitedTraderTeleport;

internal static class VisitedTraderClientState
{
    private static readonly Dictionary<string, TraderDestination> Destinations = new(StringComparer.Ordinal);

    public static AccessMode ServerAccessMode { get; private set; } = AccessMode.Personal;

    public static TravelCostSettings ServerTravelCost { get; private set; } = TravelCostSettings.Disabled();

    public static ConfirmationMode ServerConfirmation { get; private set; } = ConfirmationMode.WhenCost;

    public static IReadOnlyList<TraderDestination> GetDestinations()
    {
        return Destinations.Values
            .OrderBy(destination => destination.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(destination => destination.AreaX)
            .ThenBy(destination => destination.AreaZ)
            .ToList();
    }

    public static bool TryGet(string key, out TraderDestination destination)
    {
        return Destinations.TryGetValue(key, out destination);
    }

    public static void ApplySnapshot(
        AccessMode accessMode,
        IEnumerable<TraderDestination> destinations,
        TravelCostSettings travelCost = null,
        ConfirmationMode confirmation = ConfirmationMode.WhenCost)
    {
        ServerAccessMode = accessMode;
        ServerTravelCost = travelCost?.Clone() ?? TravelCostSettings.Disabled();
        ServerConfirmation = confirmation;
        Destinations.Clear();

        foreach (TraderDestination destination in destinations ?? Enumerable.Empty<TraderDestination>())
        {
            if (!string.IsNullOrEmpty(destination?.Key))
            {
                Destinations[destination.Key] = destination;
            }
        }
    }
}
