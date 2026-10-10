using System.Numerics;

namespace Horyzont.Core.Frames;

/// <summary>
/// A double-precision rotation quaternion for composing reference frames (K5-20). The product follows
/// <see cref="Quaternion"/>: <c>(a * b).Rotate(v) == a.Rotate(b.Rotate(v))</c>.
/// </summary>
/// <param name="X">X component.</param>
/// <param name="Y">Y component.</param>
/// <param name="Z">Z component.</param>
/// <param name="W">W (scalar) component.</param>
public readonly record struct QuaternionD(double X, double Y, double Z, double W)
{
    /// <summary>The identity rotation.</summary>
    public static QuaternionD Identity => new(0, 0, 0, 1);

    /// <summary>Gets a value indicating whether every component is finite.</summary>
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z) && double.IsFinite(W);

    /// <summary>Widens a single-precision quaternion (exact).</summary>
    /// <param name="value">The quaternion.</param>
    /// <returns>The same quaternion in double precision.</returns>
    public static QuaternionD FromFloat(Quaternion value) => new(value.X, value.Y, value.Z, value.W);

    /// <summary>Rotation by <paramref name="angle"/> radians about <paramref name="axis"/> (normalized here).</summary>
    /// <param name="axis">The axis.</param>
    /// <param name="angle">Angle in radians.</param>
    /// <returns>The rotation.</returns>
    public static QuaternionD FromAxisAngle(Vector3D axis, double angle)
    {
        var unit = Vector3D.Normalize(axis);
        var (sin, cos) = Math.SinCos(angle * 0.5);
        return new QuaternionD(unit.X * sin, unit.Y * sin, unit.Z * sin, cos);
    }

    /// <summary>Hamilton product: rotation <paramref name="b"/> followed by <paramref name="a"/>.</summary>
    /// <param name="a">Outer rotation.</param>
    /// <param name="b">Inner rotation.</param>
    /// <returns>The product.</returns>
    public static QuaternionD operator *(QuaternionD a, QuaternionD b)
    {
        var cx = (a.Y * b.Z) - (a.Z * b.Y);
        var cy = (a.Z * b.X) - (a.X * b.Z);
        var cz = (a.X * b.Y) - (a.Y * b.X);
        var dot = (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
        return new QuaternionD(
            (a.X * b.W) + (b.X * a.W) + cx,
            (a.Y * b.W) + (b.Y * a.W) + cy,
            (a.Z * b.W) + (b.Z * a.W) + cz,
            (a.W * b.W) - dot);
    }

    /// <summary>Negates every component (the same rotation).</summary>
    /// <param name="q">The quaternion.</param>
    /// <returns>The negated quaternion.</returns>
    public static QuaternionD operator -(QuaternionD q) => new(-q.X, -q.Y, -q.Z, -q.W);

    /// <summary>Hamilton product: rotation <paramref name="b"/> followed by <paramref name="a"/>.</summary>
    /// <param name="a">Outer rotation.</param>
    /// <param name="b">Inner rotation.</param>
    /// <returns>The product.</returns>
    public static QuaternionD Multiply(QuaternionD a, QuaternionD b) => a * b;

    /// <summary>Negates every component (the same rotation).</summary>
    /// <param name="q">The quaternion.</param>
    /// <returns>The negated quaternion.</returns>
    public static QuaternionD Negate(QuaternionD q) => -q;

    /// <summary>Dot product of the components.</summary>
    /// <param name="a">First.</param>
    /// <param name="b">Second.</param>
    /// <returns>The dot product.</returns>
    public static double Dot(QuaternionD a, QuaternionD b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z) + (a.W * b.W);

    /// <summary>Conjugate (the inverse of a unit quaternion).</summary>
    /// <param name="q">The quaternion.</param>
    /// <returns>The conjugate.</returns>
    public static QuaternionD Conjugate(QuaternionD q) => new(-q.X, -q.Y, -q.Z, q.W);

    /// <summary>Unit quaternion of the same rotation; identity for the zero quaternion.</summary>
    /// <param name="q">The quaternion.</param>
    /// <returns>The normalized quaternion.</returns>
    public static QuaternionD Normalize(QuaternionD q)
    {
        var length = q.Length();
        return length > 0 ? new QuaternionD(q.X / length, q.Y / length, q.Z / length, q.W / length) : Identity;
    }

    /// <summary>Spherical interpolation along the shorter arc; the result is normalized.</summary>
    /// <param name="a">Rotation at 0.</param>
    /// <param name="b">Rotation at 1.</param>
    /// <param name="t">Parameter.</param>
    /// <returns>The interpolated rotation.</returns>
    public static QuaternionD Slerp(QuaternionD a, QuaternionD b, double t)
    {
        var cos = Dot(a, b);
        if (cos < 0)
        {
            b = -b;
            cos = -cos;
        }

        double wa;
        double wb;
        if (cos > 1 - 1e-9)
        {
            // Nearly parallel: linear interpolation is exact to rounding.
            wa = 1 - t;
            wb = t;
        }
        else
        {
            var angle = Math.Acos(cos);
            var sin = Math.Sin(angle);
            wa = Math.Sin((1 - t) * angle) / sin;
            wb = Math.Sin(t * angle) / sin;
        }

        return Normalize(new QuaternionD(
            (wa * a.X) + (wb * b.X), (wa * a.Y) + (wb * b.Y), (wa * a.Z) + (wb * b.Z), (wa * a.W) + (wb * b.W)));
    }

    /// <summary>Squared length of the components.</summary>
    /// <returns>The squared length.</returns>
    public double LengthSquared() => Dot(this, this);

    /// <summary>Length of the components.</summary>
    /// <returns>The length.</returns>
    public double Length() => Math.Sqrt(LengthSquared());

    /// <summary>Rotates a vector by this (unit) quaternion.</summary>
    /// <param name="v">The vector.</param>
    /// <returns>The rotated vector.</returns>
    public Vector3D Rotate(Vector3D v)
    {
        // v' = v + 2w (u x v) + 2 u x (u x v), u = (X, Y, Z).
        var u = new Vector3D(X, Y, Z);
        var t = Vector3D.Cross(u, v) * 2;
        return v + (t * W) + Vector3D.Cross(u, t);
    }
}
