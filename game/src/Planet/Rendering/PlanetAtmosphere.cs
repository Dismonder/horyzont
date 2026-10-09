using Godot;

using Horyzont.Core.World;

namespace Horyzont.Game.Planet.Rendering;

/// <summary>
/// Glow of the atmosphere around a planet seen from orbit: a sphere slightly above the highest mountains with
/// <c>planet_atmosphere.gdshader</c>, a child of the terrain node (the planet centre). Visual only.
/// </summary>
public static class PlanetAtmosphere
{
    public const string ShaderPath = "res://shaders/planet_atmosphere.gdshader";

    /// <summary>Height of the glowing shell above the nominal radius (metres).</summary>
    public const float ShellHeightMeters = 420f;

    /// <summary>Creates the shell. Main thread, outside the tree.</summary>
    /// <param name="planet">The planet.</param>
    /// <returns>The node to add as a child of the terrain node.</returns>
    public static MeshInstance3D Create(PlanetParams planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        var shader = GD.Load<Shader>(ShaderPath) ?? throw new InvalidOperationException($"Shader {ShaderPath} not found.");
        var radius = planet.RadiusMeters + ShellHeightMeters;
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("u_radius", radius);
        return new MeshInstance3D
        {
            Name = "Atmosphere",
            Mesh = new SphereMesh { Radius = radius, Height = radius * 2f, RadialSegments = 128, Rings = 64, Material = material },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
}
