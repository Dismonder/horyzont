using Godot;

using Horyzont.Core.World;

namespace Horyzont.Game.Planet.Sky;

/// <summary>Directional light of the sun, set from <see cref="DayNightCycle.SunDirection"/>. K2-11 places it in the client scene.</summary>
public sealed partial class SunLight : DirectionalLight3D
{
    /// <summary>Points the light from the sun towards the planet centre. Main thread.</summary>
    public void Apply(ulong tick, int tickRate, PlanetParams planet)
    {
        var sun = DayNightCycle.SunDirection(tick, tickRate, planet);
        // A directional light shines along its local -Z axis.
        Basis = Basis.LookingAt(new Vector3(-sun.X, -sun.Y, -sun.Z), Vector3.Up);
    }
}
