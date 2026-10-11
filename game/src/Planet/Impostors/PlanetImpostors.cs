using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Game.Frames;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Planet.Surface;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Planet.Impostors;

/// <summary>
/// The impostors of the bodies on the client (K5-22 decision 12): once the planets are known (after <c>StarSystemState</c>), the mesh of
/// every body is built in the background (generator graph compiled once per body on the main thread, the arrays on worker threads), kept
/// per (profile, seed, R) for the life of the node, and shown under <c>Planet_&lt;id&gt;</c> when ready (hidden until then). Main thread.
/// </summary>
public sealed partial class PlanetImpostors : Node
{
    private readonly Dictionary<(string Profile, ulong Seed, float Radius, int Quads), Build> _builds = [];
    private readonly SortedDictionary<ulong, PlanetImpostor> _impostors = [];
    private Func<IReadOnlyList<PlanetParams>?> _planets = static () => null;
    private Func<FrameNodes?> _nodes = static () => null;
    private Func<PlanetId, float> _blend = static _ => 1f;
    private ILogger _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    private ShaderMaterial? _material;

    /// <summary>Wires the node.</summary>
    /// <param name="planets">The planets to build, or null while the star system is not known.</param>
    /// <param name="nodes">The client frame nodes.</param>
    /// <param name="blend">The impostor share of a body (decision 13): 1 = impostor only, 0 = voxel terrain only.</param>
    /// <param name="logger">The logger.</param>
    public void Initialize(Func<IReadOnlyList<PlanetParams>?> planets, Func<FrameNodes?> nodes, Func<PlanetId, float> blend, ILogger logger)
    {
        _planets = planets ?? throw new ArgumentNullException(nameof(planets));
        _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        _blend = blend ?? throw new ArgumentNullException(nameof(blend));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets or sets whether the camera is near a body (altitude under 2 R): its impostor has <see cref="PlanetImpostorMeshBuilder.QuadsPerSide"/>
    /// quads per face side, a far body <see cref="PlanetImpostorMeshBuilder.FarQuadsPerSide"/> (a quarter of the samples; Opus review of
    /// K5-22). A body that comes near keeps its coarse impostor until the fine one is built. All bodies are near by default.
    /// </summary>
    public Func<PlanetId, bool> Near { get; set; } = static _ => true;

    /// <summary>Gets the impostor of a body when it is built and placed.</summary>
    /// <param name="planet">The body.</param>
    /// <param name="impostor">The impostor.</param>
    /// <returns>Whether it exists.</returns>
    public bool TryGetImpostor(PlanetId planet, out PlanetImpostor? impostor)
    {
        if (_impostors.TryGetValue(planet.Value, out impostor) && IsInstanceValid(impostor))
        {
            return true;
        }

        impostor = null;
        return false;
    }

    /// <summary>Gets a value indicating whether every known body has its impostor placed.</summary>
    public bool AllPlaced
    {
        get
        {
            if (_planets() is not { } planets)
            {
                return false;
            }

            foreach (var planet in planets)
            {
                if (!TryGetImpostor(planet.Id, out _))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta) => Step();

    /// <summary>Starts missing builds, places finished ones and updates the blend of each impostor.</summary>
    public void Step()
    {
        if (_planets() is not { } planets)
        {
            return;
        }

        foreach (var planet in planets)
        {
            var quads = Near(planet.Id) ? PlanetImpostorMeshBuilder.QuadsPerSide : PlanetImpostorMeshBuilder.FarQuadsPerSide;
            var key = (planet.GeneratorProfile, planet.Seed, planet.RadiusMeters, quads);
            if (!_builds.TryGetValue(key, out var build))
            {
                var sampler = new PlanetSurfaceSampler(planet, GeneratorProfiles.CreateGenerator(planet));
                build = new Build(sampler, Task.Run(() => PlanetImpostorMeshBuilder.Build(sampler, quads)));
                _builds.Add(key, build);
            }

            if (!TryGetImpostor(planet.Id, out var impostor))
            {
                impostor = Place(planet, build);
            }
            else if (impostor!.QuadsPerSide != quads && build.Task.IsCompletedSuccessfully)
            {
                // The other resolution is ready: swap (the old one stays until then, so the body never loses its impostor).
                var old = impostor;
                _impostors.Remove(planet.Id.Value);
                old.GetParent()?.RemoveChild(old);
                old.QueueFree();
                impostor = Place(planet, build);
            }

            if (impostor is not null)
            {
                impostor.Blend = _blend(planet.Id);
            }
        }
    }

    private PlanetImpostor? Place(PlanetParams planet, Build build)
    {
        if (build.Task.IsFaulted)
        {
            if (!build.Reported)
            {
                build.Reported = true;
                PlanetImpostorLog.BuildFailed(_logger, build.Task.Exception, planet.Id.Value);
            }

            return null;
        }

        if (!build.Task.IsCompletedSuccessfully || _nodes() is not { } nodes)
        {
            return null;
        }

        _material ??= PlanetImpostorMaterial.Create();
        var impostor = PlanetImpostor.Create(planet.Id, build.Task.Result, _material);
        if (!nodes.Attach(impostor, FrameRef.Of(planet.Id)))
        {
            impostor.Free();
            return null;
        }

        _impostors[planet.Id.Value] = impostor;
        PlanetImpostorLog.Built(_logger, planet.Id.Value, impostor.BuildMilliseconds);
        return impostor;
    }

    private sealed class Build(PlanetSurfaceSampler sampler, Task<PlanetImpostorMeshData> task)
    {
        /// <summary>Gets the sampler (keeps the generator graph alive while the node lives).</summary>
        public PlanetSurfaceSampler Sampler { get; } = sampler;

        public Task<PlanetImpostorMeshData> Task { get; } = task;

        public bool Reported { get; set; }
    }
}

internal static partial class PlanetImpostorLog
{
    [LoggerMessage(EventId = 9501, Level = LogLevel.Information, Message = "Impostor of planet {Planet} built in {Milliseconds:F0} ms")]
    internal static partial void Built(ILogger logger, ulong planet, double milliseconds);

    [LoggerMessage(EventId = 9502, Level = LogLevel.Warning, Message = "Impostor of planet {Planet} failed to build")]
    internal static partial void BuildFailed(ILogger logger, Exception? exception, ulong planet);
}
