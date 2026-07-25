using UnityEngine;

namespace VisitedTraderTeleport;

internal static class Position3Extensions
{
    public static Vector3 ToVector3(this Position3 position) => new(position.X, position.Y, position.Z);

    public static Position3 ToPosition3(this Vector3 vector) => new(vector.x, vector.y, vector.z);
}
