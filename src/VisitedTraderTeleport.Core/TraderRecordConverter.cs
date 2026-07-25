namespace VisitedTraderTeleport;

internal static class TraderRecordConverter
{
    public static TraderDestinationRecord ToRecord(TraderDestination destination)
    {
        return new TraderDestinationRecord
        {
            Key = destination.Key,
            DisplayName = destination.DisplayName,
            PositionX = destination.Position.X,
            PositionY = destination.Position.Y,
            PositionZ = destination.Position.Z,
            ForwardX = destination.Forward.X,
            ForwardY = destination.Forward.Y,
            ForwardZ = destination.Forward.Z,
            AreaX = destination.AreaX,
            AreaZ = destination.AreaZ,
            Biome = destination.Biome
        };
    }

    public static TraderDestination FromRecord(TraderDestinationRecord record, string fallbackKey)
    {
        if (record == null)
        {
            return null;
        }

        return new TraderDestination
        {
            Key = string.IsNullOrEmpty(record.Key) ? fallbackKey : record.Key,
            DisplayName = record.DisplayName,
            Position = new Position3(record.PositionX, record.PositionY, record.PositionZ),
            Forward = new Position3(record.ForwardX, record.ForwardY, record.ForwardZ),
            AreaX = record.AreaX,
            AreaZ = record.AreaZ,
            Biome = record.Biome
        };
    }

    public static bool RecordsEqual(TraderDestinationRecord left, TraderDestinationRecord right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        return left.Key == right.Key &&
               left.DisplayName == right.DisplayName &&
               left.PositionX == right.PositionX &&
               left.PositionY == right.PositionY &&
               left.PositionZ == right.PositionZ &&
               left.ForwardX == right.ForwardX &&
               left.ForwardY == right.ForwardY &&
               left.ForwardZ == right.ForwardZ &&
               left.AreaX == right.AreaX &&
               left.AreaZ == right.AreaZ &&
               (left.Biome ?? string.Empty) == (right.Biome ?? string.Empty);
    }

    public static TraderDestination WithKey(TraderDestination destination, string key)
    {
        return new TraderDestination
        {
            Key = key,
            DisplayName = destination.DisplayName,
            Position = destination.Position,
            Forward = destination.Forward,
            AreaX = destination.AreaX,
            AreaZ = destination.AreaZ,
            Biome = destination.Biome
        };
    }
}
