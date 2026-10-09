using Horyzont.Core.World;

namespace Horyzont.Game.Planet.Sky;

/// <summary>
/// Day and night of a planet (gate decision D12). Planets do not rotate (K2-09), so the sun moves. The result only drives
/// lighting and never touches the simulation or saves, so unlike the generator it may use trigonometry.
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
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tickRate, 0);
        if (!float.IsFinite(planet.DayLengthSeconds) || planet.DayLengthSeconds <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(planet), planet.DayLengthSeconds, "Day length must be positive and finite.");
        }

        // Whole ticks of one day; the phase uses integer arithmetic so huge ticks lose no precision.
        var dayTicks = Math.Round((double)planet.DayLengthSeconds * tickRate);
        if (dayTicks < 1d || dayTicks > ulong.MaxValue / 2d)
        {
            throw new ArgumentOutOfRangeException(nameof(planet), planet.DayLengthSeconds, "Day length in ticks is out of range.");
        }

        var period = (ulong)dayTicks;
        var phase = tick % period;
        var angle = 2d * Math.PI * ((double)phase / period);
        return new System.Numerics.Vector3((float)Math.Cos(angle), 0f, (float)-Math.Sin(angle));
    }
}
