using Horyzont.Core.Frames;
using Horyzont.Core.World;

namespace Horyzont.Core.Space;

/// <summary>Pure star system formulas (K5-21 decisions 5 and 7): gravity of a body and the sun direction. No allocations.</summary>
public static class StarSystemMath
{
    /// <summary>
    /// Magnitude of the gravity of a body at distance <paramref name="distance"/> from its centre: <paramref name="surfaceGravity"/>
    /// up to the surface, g₀(R/r)² up to 2.5 R, then multiplied by a linear fade to zero at 3 R, and zero beyond.
    /// </summary>
    /// <param name="distance">Distance from the centre in metres.</param>
    /// <param name="radius">Surface radius R.</param>
    /// <param name="surfaceGravity">Surface gravity g₀ in m/s².</param>
    /// <returns>The magnitude in m/s².</returns>
    public static double Gravity(double distance, double radius, double surfaceGravity)
    {
        if (distance <= radius)
        {
            return surfaceGravity;
        }

        var zone = StarSystemRules.ZoneRadiusFactor * radius;
        if (distance >= zone)
        {
            return 0;
        }

        var ratio = radius / distance;
        var g = surfaceGravity * ratio * ratio;
        var fadeStart = StarSystemRules.GravityFadeStartFactor * radius;
        return distance <= fadeStart ? g : g * (zone - distance) / (zone - fadeStart);
    }

    /// <summary>Gravity acceleration in the frame of <paramref name="planet"/> (towards its centre); zero at the centre.</summary>
    /// <param name="localPosition">Position in the planet frame.</param>
    /// <param name="planet">The planet.</param>
    /// <returns>The acceleration in m/s².</returns>
    public static Vector3D GravityInPlanet(in Vector3D localPosition, PlanetParams planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        return Towards(localPosition, planet.RadiusMeters, planet.Gravity);
    }

    /// <summary>
    /// Unit direction towards the sun, the same in the system frame and in every planet frame (planets do not rotate). Same formula
    /// as the pre-K5-21 <c>DayNightCycle.SunDirection</c>: the period in whole ticks, an integer phase, tick 0 = +X, rotation
    /// around +Y, a quarter cycle later −Z. For rendering and the map only; never part of the state hash.
    /// </summary>
    /// <param name="tick">Server tick.</param>
    /// <param name="tickRate">Ticks per second.</param>
    /// <param name="cycleSeconds">Length of one sun cycle in seconds.</param>
    /// <returns>The unit direction.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Tick rate or cycle length is not positive and finite.</exception>
    public static Vector3D SunDirection(ulong tick, int tickRate, float cycleSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tickRate, 0);
        if (!float.IsFinite(cycleSeconds) || cycleSeconds <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(cycleSeconds), cycleSeconds, "Cycle length must be positive and finite.");
        }

        // Whole ticks of one cycle; the phase uses integer arithmetic so huge ticks lose no precision.
        var cycleTicks = Math.Round((double)cycleSeconds * tickRate);
        if (cycleTicks < 1d || cycleTicks > ulong.MaxValue / 2d)
        {
            throw new ArgumentOutOfRangeException(nameof(cycleSeconds), cycleSeconds, "Cycle length in ticks is out of range.");
        }

        var period = (ulong)cycleTicks;
        var phase = tick % period;
        var angle = 2d * Math.PI * ((double)phase / period);
        return new Vector3D(Math.Cos(angle), 0, -Math.Sin(angle));
    }

    /// <summary>Gravity towards a centre at <paramref name="offset"/> = position − centre.</summary>
    internal static Vector3D Towards(in Vector3D offset, double radius, double surfaceGravity)
    {
        var distance = offset.Length();
        if (distance == 0)
        {
            return Vector3D.Zero;
        }

        var g = Gravity(distance, radius, surfaceGravity);
        return g == 0 ? Vector3D.Zero : offset / distance * -g;
    }
}
