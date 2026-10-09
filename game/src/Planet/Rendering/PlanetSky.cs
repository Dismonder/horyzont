using Godot;

using Horyzont.Core.World;

namespace Horyzont.Game.Planet.Rendering;

/// <summary>
/// Sky, ambient light and haze of a planet: one <see cref="Godot.Environment"/> with <c>planet_sky.gdshader</c>. The owner calls
/// <see cref="Update"/> every frame with the camera "up" and the sun direction (world space); nothing here reaches the simulation.
/// </summary>
public sealed class PlanetSky
{
    public const string ShaderPath = "res://shaders/planet_sky.gdshader";

    private const float FogDensity = 0.00045f;

    /// <summary>Creates the environment. Main thread.</summary>
    public PlanetSky()
    {
        var shader = GD.Load<Shader>(ShaderPath) ?? throw new InvalidOperationException($"Shader {ShaderPath} not found.");
        var material = new ShaderMaterial { Shader = shader };
        Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Godot.Sky { SkyMaterial = material, RadianceSize = Godot.Sky.RadianceSizeEnum.Size128 },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Exponential,
            FogDensity = FogDensity,
            FogSkyAffect = 0f,
            FogAerialPerspective = 0.35f,
            FogSunScatter = 0.25f,
            FogHeightDensity = 0f,
            GlowEnabled = true,
            GlowIntensity = 0.6f,
            GlowBloom = 0.05f,
            GlowHdrThreshold = 1.2f,
            SsaoEnabled = true,
            SsaoRadius = 1.5f,
            SsaoIntensity = 1.4f,
        };
    }

    /// <summary>Gets the environment to put in a <see cref="WorldEnvironment"/>.</summary>
    public Godot.Environment Environment { get; }

    /// <summary>Gets the share of daylight (0 night, 1 day) after the last update.</summary>
    public float Daylight { get; private set; } = 1f;

    /// <summary>Points the sky at the camera and the sun. Main thread.</summary>
    /// <param name="up">Unit "up" of the planet at the camera (world).</param>
    /// <param name="sun">Unit direction towards the sun (world).</param>
    /// <param name="altitude">Height of the camera above the nominal radius (metres).</param>
    /// <param name="planet">The planet.</param>
    public void Update(Vector3 up, Vector3 sun, float altitude, PlanetParams planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        var elevation = (float)up.Dot(sun);
        Daylight = Mathf.SmoothStep(-0.12f, 0.2f, elevation);
        var atmosphere = 1f - Mathf.SmoothStep(planet.AtmosphereHeightMeters * 0.3f, planet.AtmosphereHeightMeters, altitude);

        var material = (ShaderMaterial)Environment.Sky.SkyMaterial;
        material.SetShaderParameter("u_up", up);
        material.SetShaderParameter("u_sun", sun);
        material.SetShaderParameter("u_atmosphere", atmosphere);
        // The stars keep their place relative to the sun, which turns about the planet axis (DayNightCycle).
        var angle = Mathf.Atan2(-sun.Z, sun.X);
        material.SetShaderParameter("u_stars", new Basis(Vector3.Up, angle));

        Environment.BackgroundEnergyMultiplier = 1f;
        Environment.AmbientLightEnergy = Mathf.Lerp(0.12f, 1f, Daylight);
        Environment.AmbientLightSkyContribution = Mathf.Lerp(0.6f, 1f, Daylight);
        Environment.FogDensity = FogDensity * atmosphere;
        var dayHaze = new Color(0.66f, 0.76f, 0.88f);
        var dusk = new Color(0.85f, 0.55f, 0.38f);
        var night = new Color(0.03f, 0.04f, 0.07f);
        var twilight = 1f - Mathf.SmoothStep(0f, 0.3f, Mathf.Abs(elevation));
        Environment.FogLightColor = night.Lerp(dayHaze, Daylight).Lerp(dusk, twilight * 0.6f);
        Environment.FogLightEnergy = Mathf.Lerp(0.3f, 1f, Daylight);
    }
}
