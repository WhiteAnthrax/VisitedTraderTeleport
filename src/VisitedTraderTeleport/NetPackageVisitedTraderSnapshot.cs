using System;
using System.Collections.Generic;
using UnityEngine;

namespace VisitedTraderTeleport;

public sealed class NetPackageVisitedTraderSnapshot : NetPackage
{
    private AccessMode accessMode = AccessMode.Personal;
    private TravelCostSettings travelCost = TravelCostSettings.Disabled();
    private ConfirmationMode confirmation = ConfirmationMode.WhenCost;
    private readonly List<TraderDestination> destinations = new();

    public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;

    internal NetPackageVisitedTraderSnapshot Setup(AccessMode mode, IEnumerable<TraderDestination> values)
    {
        accessMode = mode;
        travelCost = VisitedTraderTeleportConfig.TravelCost.Clone();
        confirmation = VisitedTraderTeleportConfig.Confirmation;
        destinations.Clear();
        if (values != null)
        {
            destinations.AddRange(values);
        }

        return this;
    }

    public override void read(PooledBinaryReader reader)
    {
        accessMode = ParseAccessMode(reader.ReadString());
        travelCost = new TravelCostSettings
        {
            Enabled = reader.ReadBoolean(),
            ItemName = reader.ReadString(),
            ItemDisplayName = reader.ReadString(),
            PerMeter = reader.ReadSingle(),
            Minimum = reader.ReadInt32()
        };
        confirmation = ParseConfirmation(reader.ReadString());
        destinations.Clear();

        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            destinations.Add(new TraderDestination
            {
                Key = reader.ReadString(),
                DisplayName = reader.ReadString(),
                Position = reader.ReadWrite(Vector3.zero).ToPosition3(),
                Forward = reader.ReadWrite(Vector3.zero).ToPosition3(),
                AreaX = reader.ReadInt32(),
                AreaZ = reader.ReadInt32(),
                Biome = reader.ReadString()
            });
        }
    }

    public override void write(PooledBinaryWriter writer)
    {
        base.write(writer);
        writer.ReadWrite(accessMode.ToString().ToLowerInvariant());
        writer.ReadWrite(travelCost.Enabled);
        writer.ReadWrite(travelCost.ItemName ?? string.Empty);
        writer.ReadWrite(travelCost.ItemDisplayName ?? string.Empty);
        writer.ReadWrite(travelCost.PerMeter);
        writer.ReadWrite(travelCost.Minimum);
        writer.ReadWrite(confirmation.ToString().ToLowerInvariant());
        writer.ReadWrite(destinations.Count);

        foreach (TraderDestination destination in destinations)
        {
            writer.ReadWrite(destination.Key ?? string.Empty);
            writer.ReadWrite(destination.DisplayName ?? string.Empty);
            writer.ReadWrite(destination.Position.ToVector3());
            writer.ReadWrite(destination.Forward.ToVector3());
            writer.ReadWrite(destination.AreaX);
            writer.ReadWrite(destination.AreaZ);
            writer.ReadWrite(destination.Biome ?? string.Empty);
        }
    }

    public override int GetLength()
    {
        int length = 20 +
                     accessMode.ToString().Length +
                     (travelCost.ItemName?.Length ?? 0) +
                     (travelCost.ItemDisplayName?.Length ?? 0) +
                     confirmation.ToString().Length;
        foreach (TraderDestination destination in destinations)
        {
            length += 40;
            length += destination.Key?.Length ?? 0;
            length += destination.DisplayName?.Length ?? 0;
            length += destination.Biome?.Length ?? 0;
        }

        return length;
    }

    public override void ProcessPackage(World world, GameManager callbacks)
    {
        VisitedTraderClientState.ApplySnapshot(accessMode, destinations, travelCost, confirmation);
        Debug.Log(
            $"[VisitedTraderTeleport] Applied server snapshot: " +
            $"{destinations.Count} destinations, mode={accessMode}, confirmation={confirmation}.");
    }

    private static AccessMode ParseAccessMode(string value)
    {
        if (Enum.TryParse(value, true, out AccessMode parsed))
        {
            return parsed;
        }

        return AccessMode.Personal;
    }

    private static ConfirmationMode ParseConfirmation(string value)
    {
        if (Enum.TryParse(value, true, out ConfirmationMode parsed))
        {
            return parsed;
        }

        return ConfirmationMode.WhenCost;
    }
}
