using Godot;

using Horyzont.Core.World;

namespace Horyzont.Game.Planet.Sky;

/// <summary>Directional light of the sun, set from <see cref="DayNightCycle.SunDirection(ulong, int, float)"/>. K2-11 places it in the client scene.</summary>
public sealed partial class SunLight : DirectionalLight3D
{
    /// <summary>Points the light from the sun towards the planet centre. Main thread.</summary>
    public void Apply(ulong tick, int tickRate, PlanetParams planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        Apply(tick, tickRate, planet.DayLengthSeconds);
    }

    /// <summary>Points the light from the sun for a sun cycle of <paramref name="cycleSeconds"/> (K5-21: the cycle of the star system). Main thread.</summary>
    public void Apply(ulong tick, int tickRate, float cycleSeconds)
    {
        var sun = DayNightCycle.SunDirection(tick, tickRate, cycleSeconds);
        // A directional light shines along its local -Z axis.
        Basis = Basis.LookingAt(new Vector3(-sun.X, -sun.Y, -sun.Z), Vector3.Up);
    }
}
