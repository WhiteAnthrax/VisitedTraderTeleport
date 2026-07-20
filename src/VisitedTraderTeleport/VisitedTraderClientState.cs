using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VisitedTraderTeleport;

internal static class VisitedTraderClientState
{
    private const float PendingTravelMaxAgeSeconds = 30f;

    private static readonly Dictionary<string, TraderDestination> Destinations = new(StringComparer.Ordinal);

    private static string pendingTravelKey;
    private static float pendingTravelRequestedAt;

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

    // The destination this client has asked the server to travel to. The heavy destination
    // pre-load waits for the server's approval package, which does not repeat the destination
    // key, so the request records it here. Only the most recent request is kept.
    public static void SetPendingTravel(string key)
    {
        pendingTravelKey = key;
        pendingTravelRequestedAt = Time.realtimeSinceStartup;
    }

    public static bool TryTakePendingTravel(out TraderDestination destination)
    {
        destination = null;
        string key = pendingTravelKey;
        pendingTravelKey = null;
        if (string.IsNullOrEmpty(key) ||
            Time.realtimeSinceStartup - pendingTravelRequestedAt > PendingTravelMaxAgeSeconds)
        {
            return false;
        }

        return TryGet(key, out destination);
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
