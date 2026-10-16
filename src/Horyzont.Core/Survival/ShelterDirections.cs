using System.Numerics;

namespace Horyzont.Core.Survival;

/// <summary>The 26 directions of the shelter rays (K6-34, D11).</summary>
public static class ShelterDirections
{
    /// <summary>Number of directions.</summary>
    public const int Count = 26;

    private static readonly Vector3[] _local = Build();

    /// <summary>Gets the index of the direction straight up.</summary>
    public static int UpIndex => 21;

    /// <summary>
    /// The unit direction <c>(dx, dy, dz)</c> of index <paramref name="index"/> in the local basis: the 26 vectors of {-1, 0, 1}^3 without the origin, normalised, in the order of the loops
    /// <c>dy</c>, <c>dz</c>, <c>dx</c> from -1.
    /// </summary>
    /// <param name="index">0 to 25.</param>
    /// <returns>The direction with <c>y</c> as up.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not 0 to 25.</exception>
    public static Vector3 Local(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
        return _local[index];
    }

    /// <summary>
    /// The local basis at a point: <c>y = up</c>, <c>x = normalize(cross(up, ref))</c>, <c>z = cross(x, y)</c>, with <c>ref = (1, 0, 0)</c> and <c>ref = (0, 0, 1)</c> when <c>|up.X| &gt; 0.9</c>.
    /// </summary>
    /// <param name="up">The unit up direction.</param>
    /// <param name="x">The first axis.</param>
    /// <param name="y">The second axis, equal to <paramref name="up"/>.</param>
    /// <param name="z">The third axis.</param>
    public static void Basis(Vector3 up, out Vector3 x, out Vector3 y, out Vector3 z)
    {
        var reference = MathF.Abs(up.X) > 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        y = up;
        x = Vector3.Normalize(Vector3.Cross(up, reference));
        z = Vector3.Cross(x, y);
    }

    /// <summary>The direction of index <paramref name="index"/> in the frame of the planet at a point with the given up direction.</summary>
    /// <param name="index">0 to 25.</param>
    /// <param name="up">The unit up direction.</param>
    /// <returns>The unit direction.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not 0 to 25.</exception>
    public static Vector3 Direction(int index, Vector3 up)
    {
        var local = Local(index);
        Basis(up, out var x, out var y, out var z);
        return (x * local.X) + (y * local.Y) + (z * local.Z);
    }

    private static Vector3[] Build()
    {
        var result = new Vector3[Count];
        var n = 0;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dz = -1; dz <= 1; dz++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0 && dz == 0)
                    {
                        continue;
                    }

                    result[n++] = Vector3.Normalize(new Vector3(dx, dy, dz));
                }
            }
        }

        return result;
    }
}
