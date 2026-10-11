using System.Numerics;

namespace Horyzont.Client.Navigation;

/// <summary>
/// Finds the height of the planet surface along a ray from the centre by bisection of a signed distance field (K5-27 decision 12):
/// inside the surface the field is not positive, outside it is positive. Between R − relief and R + relief down to the tolerance it takes
/// at most 11 samples for 300 m and 0.5 m (12 when the search ends at an end of the range and that end is checked). Used by the planet map; the sampler of the game supplies the field.
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
        var rangeLow = radius - relief;
        var rangeHigh = radius + relief;
        var low = rangeLow;
        var high = rangeHigh;
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

        // The search converges on an end of the range both when the surface is there and when the field never changes sign
        // (all air goes to the top, all solid to the bottom); one extra sample at that end tells the two apart.
        if (low <= rangeLow)
        {
            samples++;
            if (sdf(direction * rangeLow) > 0f)
            {
                return radius;
            }
        }
        else if (high >= rangeHigh)
        {
            samples++;
            if (sdf(direction * rangeHigh) <= 0f)
            {
                return radius;
            }
        }

        return 0.5f * (low + high);
    }

    /// <summary>
    /// Finds the outermost surface along a ray in a field that has air pockets (caves) below the crust. Plain bisection can take a cave for
    /// the open air and settle on a deeper surface, so the ray is first scanned from the top in steps shorter than the cave-free crust, and only
    /// the last step is bisected.
    /// </summary>
    /// <param name="sdf">The field at a point of the planet frame; positive in the air.</param>
    /// <param name="direction">Unit direction from the centre.</param>
    /// <param name="radius">Nominal radius of the planet.</param>
    /// <param name="crust">Thickness below the surface that is solid without pockets, metres; the scan step is below it.</param>
    /// <param name="samples">Receives the number of field samples used.</param>
    /// <param name="relief">Search range around the radius.</param>
    /// <param name="tolerance">Accuracy.</param>
    /// <returns>The radius of the outermost surface; the nominal radius when the ray has no air-to-solid change in the range.</returns>
    public static float FindOutermostSurfaceRadius(
        Func<Vector3, float> sdf, Vector3 direction, float radius, float crust, out int samples, float relief = DefaultRelief, float tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(sdf);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(crust, 0f);
        var step = 0.9f * crust;
        var rangeLow = radius - relief;
        var high = radius + relief;
        samples = 1;
        if (sdf(direction * high) <= 0f)
        {
            return radius;
        }

        while (high > rangeLow)
        {
            var low = MathF.Max(high - step, rangeLow);
            samples++;
            if (sdf(direction * low) <= 0f)
            {
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

            high = low;
        }

        return radius;
    }
}
