using System;

namespace VisitedTraderTeleport;

internal readonly struct Position3 : IEquatable<Position3>
{
    public readonly float X;
    public readonly float Y;
    public readonly float Z;

    public Position3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static readonly Position3 Zero = new(0f, 0f, 0f);
    public static readonly Position3 Forward = new(0f, 0f, 1f);

    public bool Equals(Position3 other) => X == other.X && Y == other.Y && Z == other.Z;

    public override bool Equals(object obj) => obj is Position3 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
}
