using System.Numerics;

namespace Horyzont.Core.Frames;

/// <summary>A double-precision vector for composing reference frames (K5-20). Metres or metres per second.</summary>
/// <param name="X">X component.</param>
/// <param name="Y">Y component.</param>
/// <param name="Z">Z component.</param>
public readonly record struct Vector3D(double X, double Y, double Z)
{
    /// <summary>The zero vector.</summary>
    public static Vector3D Zero => default;

    /// <summary>Gets a value indicating whether every component is finite.</summary>
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);

    /// <summary>Widens a single-precision vector (exact).</summary>
    /// <param name="value">The vector.</param>
    /// <returns>The same vector in double precision.</returns>
    public static Vector3D FromFloat(Vector3 value) => new(value.X, value.Y, value.Z);

    /// <summary>Adds two vectors.</summary>
    /// <param name="a">First.</param>
    /// <param name="b">Second.</param>
    /// <returns>The sum.</returns>
    public static Vector3D operator +(Vector3D a, Vector3D b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    /// <summary>Subtracts two vectors.</summary>
    /// <param name="a">First.</param>
    /// <param name="b">Second.</param>
    /// <returns>The difference.</returns>
    public static Vector3D operator -(Vector3D a, Vector3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    /// <summary>Negates a vector.</summary>
    /// <param name="a">The vector.</param>
    /// <returns>The negated vector.</returns>
    public static Vector3D operator -(Vector3D a) => new(-a.X, -a.Y, -a.Z);

    /// <summary>Scales a vector.</summary>
    /// <param name="a">The vector.</param>
    /// <param name="s">The scale.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector3D operator *(Vector3D a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    /// <summary>Scales a vector.</summary>
    /// <param name="s">The scale.</param>
    /// <param name="a">The vector.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector3D operator *(double s, Vector3D a) => new(a.X * s, a.Y * s, a.Z * s);

    /// <summary>Divides a vector by a scalar.</summary>
    /// <param name="a">The vector.</param>
    /// <param name="s">The divisor.</param>
    /// <returns>The divided vector.</returns>
    public static Vector3D operator /(Vector3D a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    /// <summary>Adds two vectors.</summary>
    /// <param name="a">First.</param>
    /// <param name="b">Second.</param>
    /// <returns>The sum.</returns>
    public static Vector3D Add(Vector3D a, Vector3D b) => a + b;

    /// <summary>Subtracts two vectors.</summary>
    /// <param name="a">First.</param>
    /// <param name="b">Second.</param>
    /// <returns>The difference.</returns>
    public static Vector3D Subtract(Vector3D a, Vector3D b) => a - b;

    /// <summary>Negates a vector.</summary>
    /// <param name="a">The vector.</param>
    /// <returns>The negated vector.</returns>
    public static Vector3D Negate(Vector3D a) => -a;

    /// <summary>Scales a vector.</summary>
    /// <param name="a">The vector.</param>
    /// <param name="s">The scale.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector3D Multiply(Vector3D a, double s) => a * s;

    /// <summary>Divides a vector by a scalar.</summary>
    /// <param name="a">The vector.</param>
    /// <param name="s">The divisor.</param>
    /// <returns>The divided vector.</returns>
    public static Vector3D Divide(Vector3D a, double s) => a / s;

    /// <summary>Dot product.</summary>
    /// <param name="a">First.</param>
    /// <param name="b">Second.</param>
    /// <returns>The dot product.</returns>
    public static double Dot(Vector3D a, Vector3D b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    /// <summary>Cross product.</summary>
    /// <param name="a">First.</param>
    /// <param name="b">Second.</param>
    /// <returns>The cross product.</returns>
    public static Vector3D Cross(Vector3D a, Vector3D b) =>
        new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

    /// <summary>Linear interpolation.</summary>
    /// <param name="a">Value at 0.</param>
    /// <param name="b">Value at 1.</param>
    /// <param name="t">Parameter.</param>
    /// <returns>The interpolated vector.</returns>
    public static Vector3D Lerp(Vector3D a, Vector3D b, double t) => a + ((b - a) * t);

    /// <summary>Distance between two points.</summary>
    /// <param name="a">First.</param>
    /// <param name="b">Second.</param>
    /// <returns>The distance.</returns>
    public static double Distance(Vector3D a, Vector3D b) => (a - b).Length();

    /// <summary>Unit vector in the direction of <paramref name="a"/>; zero for the zero vector.</summary>
    /// <param name="a">The vector.</param>
    /// <returns>The normalized vector.</returns>
    public static Vector3D Normalize(Vector3D a)
    {
        var length = a.Length();
        return length > 0 ? a / length : Zero;
    }

    /// <summary>Squared length.</summary>
    /// <returns>The squared length.</returns>
    public double LengthSquared() => Dot(this, this);

    /// <summary>Length.</summary>
    /// <returns>The length.</returns>
    public double Length() => Math.Sqrt(LengthSquared());
}
