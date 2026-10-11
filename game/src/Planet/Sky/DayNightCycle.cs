using Horyzont.Core.Space;
using Horyzont.Core.World;

namespace Horyzont.Game.Planet.Sky;

/// <summary>
/// Day and night of a planet (gate decision D12). Planets do not rotate (K2-09), so the sun moves. The result only drives
/// lighting and never touches the simulation or saves, so unlike the generator it may use trigonometry. Since K5-21 the formula lives in
/// <see cref="StarSystemMath.SunDirection"/> (the sun cycle of the star system); results are bitwise the same.
/// </summary>
public static class DayNightCycle
{
    /// <summary>
    /// Unit direction from the planet centre towards the sun, in the planet frame, for a server tick. Pure and stateless:
    /// the same tick gives the same direction on every machine and in every mode. Tick 0 is the sun on +X (noon at the
    /// start point on the equator); the sun rotates around +Y (poles on +/-Y), a quarter of a day later it is on -Z.
    /// </summary>
    /// <param name="tick">Server tick.</param>
    /// <param name="tickRate">Ticks per second (from the server hello).</param>
    /// <param name="planet">Planet whose day length is used.</param>
    /// <exception cref="ArgumentOutOfRangeException">Tick rate or day length is not positive and finite.</exception>
    public static System.Numerics.Vector3 SunDirection(ulong tick, int tickRate, PlanetParams planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        return SunDirection(tick, tickRate, planet.DayLengthSeconds);
    }

    /// <summary>
    /// Unit direction towards the sun for a sun cycle of <paramref name="cycleSeconds"/> (K5-21 decision 7): the same in the system
    /// frame and in every planet frame, since planets do not rotate. Delegates to <see cref="StarSystemMath.SunDirection"/>.
    /// </summary>
    /// <param name="tick">Server tick.</param>
    /// <param name="tickRate">Ticks per second (from the server hello).</param>
    /// <param name="cycleSeconds">Length of the sun cycle in seconds.</param>
    /// <exception cref="ArgumentOutOfRangeException">Tick rate or cycle length is not positive and finite.</exception>
    public static System.Numerics.Vector3 SunDirection(ulong tick, int tickRate, float cycleSeconds)
    {
        var sun = StarSystemMath.SunDirection(tick, tickRate, cycleSeconds);
        return new System.Numerics.Vector3((float)sun.X, (float)sun.Y, (float)sun.Z);
    }
}
