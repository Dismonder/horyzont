using System.Numerics;

namespace Horyzont.Client.Navigation;

/// <summary>
/// Finds the height of the planet surface along a ray from the centre by bisection of a signed distance field (K5-27 decision 12):
/// inside the surface the field is not positive, outside it is positive. Between R − relief and R + relief down to the tolerance it takes
/// at most 11 samples for 300 m and 0.5 m. Used by the planet map; the sampler of the game supplies the field.
/// </summary>
public static class SurfaceBisection
{
    /// <summary>Largest relief of the planet generator around the nominal radius, metres.</summary>
    public const float DefaultRelief = 300f;

    /// <summary>Accuracy of the height, metres.</summary>
    public const float DefaultTolerance = 0.5f;

    /// <summary>Finds the radius at which the field changes sign along <paramref name="direction"/>.</summary>
    /// <param name="sdf">The field at a point of the planet frame; positive in the air.</param>
    /// <param name="direction">Unit direction from the centre.</param>
    /// <param name="radius">Nominal radius of the planet.</param>
    /// <param name="samples">Receives the number of field samples used.</param>
    /// <param name="relief">Search range around the radius.</param>
    /// <param name="tolerance">Accuracy.</param>
    /// <returns>The radius of the surface; the nominal radius when the field does not change sign in the range.</returns>
    public static float FindSurfaceRadius(
        Func<Vector3, float> sdf, Vector3 direction, float radius, out int samples, float relief = DefaultRelief, float tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(sdf);
        var low = radius - relief;
        var high = radius + relief;
        samples = 0;
        while (high - low > tolerance)
        {
            var middle = 0.5f * (low + high);
            samples++;
            if (sdf(direction * middle) <= 0f)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return 0.5f * (low + high);
    }
}
