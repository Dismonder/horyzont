using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Props;

namespace Horyzont.Testing;

/// <summary>Helpers that find props of the layout in the tests of K6-31.</summary>
public static class PropFixtures
{
    private const float LargestSearchRadiusMeters = 512f;

    /// <summary>
    /// The first prop of a kind (lowest <see cref="PropId"/>) whose anchor lies within a growing sphere around <paramref name="near"/>
    /// (64 m, then doubled up to 512 m). The layout is frozen, so the answer is the same on every run; fails the test when there is none.
    /// </summary>
    public static PropInstance FindProp(PropField field, PlanetId planet, Vector3 near, PropKindV1 kind)
    {
        ArgumentNullException.ThrowIfNull(field);
        var found = new List<PropInstance>();
        for (var radius = 64f; radius <= LargestSearchRadiusMeters; radius *= 2)
        {
            found.Clear();
            field.CollectInSphere(planet, near, radius, found);
            foreach (var instance in found)
            {
                if (instance.Kind == kind)
                {
                    return instance;
                }
            }
        }

        throw new InvalidOperationException($"No prop of kind {kind} within {LargestSearchRadiusMeters} m of {near}; found {found.Count} props of other kinds.");
    }

    /// <summary>
    /// A point on the sphere of <paramref name="surfaceRadius"/> at a horizontal distance from a prop's anchor, in a fixed direction
    /// (perpendicular to the up vector and to the X axis, or to the Z axis near the poles of the X axis).
    /// </summary>
    public static Vector3 PointBeside(PropInstance prop, float horizontalMeters, float surfaceRadius)
    {
        var moved = prop.Anchor + (SideOf(prop) * horizontalMeters);
        return Vector3.Normalize(moved) * surfaceRadius;
    }

    /// <summary>The unit vector used by <see cref="PointBeside"/>: tangent to the surface at the anchor.</summary>
    public static Vector3 SideOf(PropInstance prop)
    {
        var axis = MathF.Abs(prop.Up.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ;
        return Vector3.Normalize(Vector3.Cross(prop.Up, axis));
    }
}
