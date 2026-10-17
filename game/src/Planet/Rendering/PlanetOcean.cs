using Godot;

using Horyzont.Core.World;

namespace Horyzont.Game.Planet.Rendering;

/// <summary>
/// The visual sea of a planet: a finely tessellated sphere at <see cref="PlanetLook.SeaLevelMeters"/> with
/// <c>planet_ocean.gdshader</c>, a child of the terrain node (the planet centre). No collisions and no state.
/// </summary>
public static class PlanetOcean
{
    public const string ShaderPath = "res://shaders/planet_ocean.gdshader";

    // 512 x 256 segments keep the flat facets within 0.1 m of the true sphere on a 4 km planet.
    private const int RadialSegments = 512;
    private const int Rings = 256;

    /// <summary>Creates the sea node. Main thread, outside the tree.</summary>
    /// <param name="planet">The planet.</param>
    /// <returns>The node to add as a child of the terrain node.</returns>
    public static MeshInstance3D Create(PlanetParams planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        var shader = GD.Load<Shader>(ShaderPath) ?? throw new InvalidOperationException($"Shader {ShaderPath} not found.");
        var radius = planet.RadiusMeters + PlanetLook.SeaLevelMeters;
        var mesh = new SphereMesh
        {
            Radius = radius,
            Height = radius * 2f,
            RadialSegments = RadialSegments,
            Rings = Rings,
            Material = new ShaderMaterial { Shader = shader },
        };
        return new MeshInstance3D
        {
            Name = "Ocean",
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
}
