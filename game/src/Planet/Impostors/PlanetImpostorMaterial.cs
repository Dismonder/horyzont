using Godot;

namespace Horyzont.Game.Planet.Impostors;

/// <summary>
/// Material of the planet impostors (K5-22 decision 12): <c>planet_impostor.gdshader</c> — vertex colours and Lambert lighting from the
/// sun, model coordinates only. Create it once and share it between bodies; main thread.
/// </summary>
public static class PlanetImpostorMaterial
{
    /// <summary>The shader resource path.</summary>
    public const string ShaderPath = "res://shaders/planet_impostor.gdshader";

    /// <summary>Builds the material.</summary>
    /// <returns>The material.</returns>
    public static ShaderMaterial Create()
    {
        var shader = GD.Load<Shader>(ShaderPath) ?? throw new InvalidOperationException($"Shader {ShaderPath} not found.");
        return new ShaderMaterial { Shader = shader };
    }
}
