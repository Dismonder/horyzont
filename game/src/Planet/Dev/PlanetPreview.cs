using System.Diagnostics;
using System.Globalization;

using Godot;

using Horyzont.Core.World;
using Horyzont.Game.Planet.Diagnostics;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Planet.Rendering;
using Horyzont.Game.Planet.Sky;

namespace Horyzont.Game.Planet.Dev;

/// <summary>
/// <c>--planet-preview [--seed hex] [--screenshots dir]</c>: a tool to judge the generator (needs graphics). Shows the home planet
/// from four fixed points above the direction +X, waits until the terrain has no pending work, logs the load time and saves a
/// PNG per point. The controlled development camera is K2-11, not this.
/// </summary>
public sealed partial class PlanetPreview : Node
{
    private const int StableFramesRequired = 20;
    private const double LoadTimeoutSeconds = 60.0;

    private readonly ulong _galaxySeed;
    private readonly string? _screenshotDirectory;
    private readonly float? _lodDistance;

    /// <summary>Creates the mode node.</summary>
    /// <param name="galaxySeed">Galaxy seed of the previewed world.</param>
    /// <param name="screenshotDirectory">Where to save PNGs, or null.</param>
    /// <param name="lodDistance">Override of the LOD 0 ring distance for tuning, or null for the factory value.</param>
    public PlanetPreview(ulong galaxySeed, string? screenshotDirectory, float? lodDistance = null)
    {
        _galaxySeed = galaxySeed;
        _screenshotDirectory = screenshotDirectory;
        _lodDistance = lodDistance;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.PushError("--planet-preview needs graphics; do not use --headless.");
            GD.Print("PLANET PREVIEW NEEDS GRAPHICS");
            GetTree().Quit(Boot.DevModes.ExitBadArguments);
            return;
        }

        _ = RunAsync();
    }

    private static Vector3 Direction() => new(1f, 0f, 0f);

    private async Task RunAsync()
    {
        try
        {
            await ShowAsync();
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            // Dev tool: report any failure on stderr and in the exit code instead of hanging the process.
            GD.PushError($"Planet preview failed: {ex}");
            GD.Print($"PLANET PREVIEW FAILED: {ex.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task ShowAsync()
    {
        var planet = PlanetGenesis.CreatePlanet(_galaxySeed, PlanetGenesis.HomePlanetIndex, GeneratorProfiles.Current);
        var surface = FindSurfaceRadius(planet);
        GD.Print($"PLANET PREVIEW seed=0x{_galaxySeed:X16} id=0x{planet.Id.Value:X16} surface_x={surface.ToString("F1", CultureInfo.InvariantCulture)}");

        var environment = new WorldEnvironment { Environment = CreateEnvironment() };
        AddChild(environment);
        var sun = new SunLight { ShadowEnabled = false };
        AddChild(sun);
        sun.Apply(0, 60, planet);

        var terrain = PlanetTerrainFactory.Create(planet, PlanetTerrainOptions.Preview, PlanetTerrainMaterial.Create());
        if (_lodDistance is { } lodDistance)
        {
            terrain.SetLodDistance(lodDistance);
        }

        AddChild(terrain);
        GD.Print($"PREVIEW lod_distance={terrain.GetLodDistance().ToString("F0", CultureInfo.InvariantCulture)} lod_count={terrain.GetLodCount()}");

        var camera = new Camera3D { Near = 0.05f, Far = 60000f, Current = true };
        AddChild(camera);
        var viewer = new VoxelViewer { RequiresCollisions = false, RequiresVisuals = true };
        camera.AddChild(viewer);

        var up = Direction();
        (string Name, Vector3 Position, Vector3 Target, Vector3 Up, uint ViewDistance)[] points =
        [
            ("orbit", up * 12000f, Vector3.Zero, Vector3.Up, 16384),
            ("high", up * (surface + 2000f), up * surface + new Vector3(0f, 0f, 2500f), up, 8192),
            ("mid", up * (surface + 100f), up * (surface + 40f) + new Vector3(0f, 0f, 700f), up, 4096),
            ("ground", up * (surface + 2f), up * (surface + 2f) + new Vector3(0f, 0f, 100f), up, 2048),
        ];

        var index = 1;
        foreach (var (name, position, target, pointUp, viewDistance) in points)
        {
            viewer.ViewDistance = viewDistance;
            camera.Position = position;
            camera.LookAt(target, pointUp);
            var elapsed = await WaitForTerrainAsync();
            GD.Print($"PREVIEW point={name} load_ms={elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)} data_blocks={terrain.DebugGetDataBlockCount()} mesh_blocks={terrain.DebugGetMeshBlockCount()}");
            if (_screenshotDirectory is not null)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
                var image = GetViewport().GetTexture().GetImage();
                var path = Path.Combine(_screenshotDirectory, $"planet-preview-{index}-{name}.png");
                var error = image.SavePng(path);
                if (error != Error.Ok)
                {
                    throw new IOException($"Saving {path} failed: {error}");
                }

                GD.Print($"PREVIEW screenshot={path}");
            }

            index++;
        }
    }

    /// <summary>Radius of the surface above +X (outermost solid voxel), found with the generator itself.</summary>
    private static float FindSurfaceRadius(PlanetParams planet)
    {
        using var probe = new GeneratorProbe(GeneratorProfiles.CreateGenerator(planet));
        for (var x = (int)(planet.RadiusMeters + PlanetGeneratorV1.MaxReliefMeters + 16); x > planet.RadiusMeters - PlanetGeneratorV1.MaxReliefMeters - 16; x--)
        {
            if (probe.Sample(x, 0, 0).Sdf < 0f)
            {
                return x;
            }
        }

        throw new InvalidOperationException("No surface found above +X.");
    }

    private static Godot.Environment CreateEnvironment() => new()
    {
        // A flat colour instead of a procedural sky: the planet frame has its up on +X at the preview points, which
        // a sky oriented along +Y would not match.
        BackgroundMode = Godot.Environment.BGMode.Color,
        BackgroundColor = new Color(0.42f, 0.58f, 0.80f),
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = new Color(0.62f, 0.66f, 0.74f),
        AmbientLightEnergy = 0.8f,
        TonemapMode = Godot.Environment.ToneMapper.Filmic,
    };

    /// <summary>Waits until the voxel engine has no pending work for a number of frames (or the timeout passes).</summary>
    private async Task<TimeSpan> WaitForTerrainAsync()
    {
        var clock = Stopwatch.StartNew();
        var stable = 0;
        var frames = 0;
        while (stable < StableFramesRequired && clock.Elapsed.TotalSeconds < LoadTimeoutSeconds)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            frames++;
            var tasks = (Godot.Collections.Dictionary)VoxelEngine.GetStats()["tasks"];
            var pending = (int)tasks["streaming"] + (int)tasks["meshing"] + (int)tasks["generation"] + (int)tasks["main_thread"];
            stable = frames > 5 && pending == 0 ? stable + 1 : 0;
        }

        if (stable < StableFramesRequired)
        {
            GD.PushWarning($"Terrain still loading after {LoadTimeoutSeconds} s.");
        }

        return clock.Elapsed;
    }
}
