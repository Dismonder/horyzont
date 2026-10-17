using Godot;

using Horyzont.Core.World;

namespace Horyzont.Game.Planet.Rendering;

/// <summary>
/// The drifting cloud layer of a planet: a sphere inside the atmosphere with <c>planet_clouds.gdshader</c>, a child of the terrain
/// node (the planet centre). It casts no shadow (a closed sphere would shade the whole planet) and holds no state.
/// </summary>
public static class PlanetClouds
{
    public const string ShaderPath = "res://shaders/planet_clouds.gdshader";

    /// <summary>Height of the cloud layer above the nominal radius (metres), above the highest mountains.</summary>
    public const float LayerHeightMeters = 450f;

    /// <summary>Creates the cloud layer. Main thread, outside the tree.</summary>
    /// <param name="planet">The planet.</param>
    /// <returns>The node to add as a child of the terrain node.</returns>
    public static MeshInstance3D Create(PlanetParams planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        var shader = GD.Load<Shader>(ShaderPath) ?? throw new InvalidOperationException($"Shader {ShaderPath} not found.");
        var radius = planet.RadiusMeters + LayerHeightMeters;
        return new MeshInstance3D
        {
            Name = "Clouds",
            Mesh = new SphereMesh
            {
                Radius = radius,
                Height = radius * 2f,
                RadialSegments = 256,
                Rings = 128,
                Material = new ShaderMaterial { Shader = shader },
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
}
