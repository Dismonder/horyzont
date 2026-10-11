using Godot;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Game.Frames;
using Horyzont.Game.Planet;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Planet.Surface;
using Horyzont.Game.Terrain;
using Horyzont.Server;
using Horyzont.Server.Space;

using Microsoft.Extensions.Logging;

using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Server.Terrain;

/// <summary>
/// The server terrains of the planets, created and removed on demand (K5-22 decisions 2–4, 6): applies
/// <see cref="PlanetActivityPolicy"/> in the <see cref="ServerTickPhase.Compose"/> phase (after the frame history, so it sees the poses of
/// this tick), holds the leases and answers <see cref="IPlanetTerrainReadiness"/>. Generators are compiled once at start for every body
/// and kept (an activation never compiles a graph on the main thread); the mipmap rebuilder of each body is made once. A terrain node
/// goes under the planet node of <see cref="FrameNodes"/> (<c>Planet_&lt;id&gt;</c>, identity transform, so its local space is the planet
/// frame); before the frame nodes have a world, under the fallback parent at the placement of the planet. The home planet is active
/// from the start and always. Main thread.
/// </summary>
public sealed partial class ServerPlanetTerrains : IServerTickParticipant, IPlanetTerrainReadiness
{
    /// <summary>Space over <c>R + MaxRelief</c> where nothing is built (above the voxel bounds of <see cref="PlanetTerrainLayout"/>).</summary>
    public const float ClearMarginMeters = 64f;

    /// <summary>
    /// Half-size of the box around the generated ground under the query centre that must have its LOD 0 collision built (well inside the
    /// LOD 0 ring of <see cref="PlanetTerrainFactory.LodDistance"/> around a viewer standing on that ground).
    /// </summary>
    public const float GroundCoreMeters = 40f;

    private readonly VoxelTerrainService _service;
    private readonly WorldState _state;
    private readonly Node3D _fallbackParent;
    private readonly Func<PlanetParams, VoxelGenerator, VoxelLodTerrain> _create;
    private readonly ILogger<ServerPlanetTerrains> _logger;
    private readonly Horyzont.Core.Space.WorldStatePlanetPlacement _placement;
    private readonly SortedDictionary<ulong, Body> _bodies = [];
    private int _replays;

    /// <summary>Initializes a new instance of the <see cref="ServerPlanetTerrains"/> class; <see cref="ActivateHome"/> creates the first terrain.</summary>
    /// <param name="service">The terrain service.</param>
    /// <param name="state">The loaded world.</param>
    /// <param name="mipmaps">Creates the mipmap rebuilder of a body from its generator; null for session mipmaps only.</param>
    /// <param name="fallbackParent">Parent of terrain nodes while the frame nodes have no world (in the scene tree, at the origin).</param>
    /// <param name="create">Creates the configured terrain node of a planet with its generator (outside the tree, no stream).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="options">Activation settings; defaults when null.</param>
    public ServerPlanetTerrains(
        VoxelTerrainService service, WorldState state, Func<PlanetParams, VoxelGenerator, DerivedLodRebuilder?>? mipmaps, Node3D fallbackParent,
        Func<PlanetParams, VoxelGenerator, VoxelLodTerrain> create, ILogger<ServerPlanetTerrains> logger, PlanetActivityOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(fallbackParent);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(logger);
        _service = service;
        _state = state;
        _fallbackParent = fallbackParent;
        _create = create;
        _logger = logger;
        _placement = new Horyzont.Core.Space.WorldStatePlanetPlacement(state);
        Policy = new PlanetActivityPolicy(options);
        ShipViewers = new ServerShipViewers(fallbackParent);
        foreach (var planet in state.Planets.All)
        {
            var generator = GeneratorProfiles.CreateGenerator(planet);
            _bodies.Add(planet.Id.Value, new Body(planet, generator, mipmaps?.Invoke(planet, generator), new PlanetSurfaceSampler(planet, generator)));
        }
    }

    /// <summary>Test seam: called with each node right after the terrain service made it, before it enters the tree.</summary>
    internal Action<PlanetTerrain>? Created { get; init; }

    /// <summary>Activates the home planet (always active, P2); the factory calls it once after construction.</summary>
    public void ActivateHome() => Activate(_state.HomePlanet);

    /// <inheritdoc />
    public ServerTickPhase Phase => ServerTickPhase.Compose;

    /// <summary>Gets the activation policy.</summary>
    public PlanetActivityPolicy Policy { get; }

    /// <summary>Gets or sets the frame nodes of the server; terrain nodes go under their planet nodes.</summary>
    public FrameNodes? FrameNodes { get; set; }

    /// <summary>Gets or sets the frame graph of the running world; the policy waits for it.</summary>
    public Func<FrameGraph?> Graph { get; set; } = static () => null;

    /// <summary>Gets or sets the demand of ship-mode constructs (K5-22 <see cref="MovingShipDemand"/>, K5-24 replaces it).</summary>
    public IShipDemandSource? ShipDemand { get; set; }

    /// <summary>Gets or sets the frame history of the running world (ship speeds for <see cref="ShipViewers"/>); no ship viewers while null.</summary>
    public Horyzont.Server.Frames.ServerFrameHistory? History { get; set; }

    /// <summary>Gets or sets the seconds per server tick.</summary>
    public double TickSeconds { get; set; } = 1.0 / 60.0;

    /// <summary>Gets the collision viewers of ships in the frames of active planets (decision 5).</summary>
    public ServerShipViewers ShipViewers { get; }

    /// <summary>Gets the number of planets with a terrain node.</summary>
    public int ActiveCount
    {
        get
        {
            var count = 0;
            foreach (var body in _bodies.Values)
            {
                count += body.Node is null ? 0 : 1;
            }

            return count;
        }
    }

    /// <summary>Gets a value indicating whether a replay holds the terrains (no deactivation while it runs).</summary>
    public bool IsReplaying => _replays > 0;

    /// <summary>Gets a value indicating whether a planet has a terrain node.</summary>
    /// <param name="planet">The planet.</param>
    /// <returns>True when active.</returns>
    public bool IsActive(PlanetId planet) => _bodies.TryGetValue(planet.Value, out var body) && body.Node is not null;

    /// <summary>The terrain node of an active planet.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="node">The node.</param>
    /// <returns>False for an inactive planet.</returns>
    public bool TryGetNode(PlanetId planet, out PlanetTerrain? node)
    {
        node = _bodies.TryGetValue(planet.Value, out var body) ? body.Node : null;
        return node is not null;
    }

    /// <summary>
    /// Holds a planet active until the lease is disposed (K5-22 decision 4); an inactive planet is activated at once. Used by the replay,
    /// <c>place</c> and dev tools; flight takes no leases.
    /// </summary>
    /// <param name="planet">The planet.</param>
    /// <param name="reason">Why (logged).</param>
    /// <returns>The lease.</returns>
    /// <exception cref="ArgumentException">Unknown planet.</exception>
    public PlanetTerrainLease Acquire(PlanetId planet, PlanetLeaseReason reason)
    {
        Policy.AddLease(_state, planet);
        Activate(planet);

        LogLeaseAcquired(_logger, planet.Value, reason);
        return new PlanetTerrainLease(this, planet, reason);
    }

    /// <summary>Marks a replay running: deactivation is refused until the returned scope is disposed.</summary>
    /// <returns>The scope.</returns>
    public IDisposable BeginReplay()
    {
        _replays++;
        return new ReplayScope(this);
    }

    /// <summary>
    /// Readiness of the terrain in a query sphere (README Y11). Voxel Tools builds LOD 0 (the only collision LOD) only within
    /// <see cref="PlanetTerrainFactory.LodDistance"/> of a viewer, so a column sphere of hundreds of metres is never meshed at LOD 0 as a
    /// whole; the check is made where the ship meets the ground: the generated ground under the centre (<see cref="PlanetSurfaceSampler"/>)
    /// with a box of <see cref="GroundCoreMeters"/> (at most the query radius). A sphere whose radial span does not reach that ground is
    /// ready (nothing to collide with in it), unless the stream faulted. Main thread.
    /// </summary>
    /// <param name="planet">The planet.</param>
    /// <param name="localCenter">Centre of the sphere in the planet frame.</param>
    /// <param name="radius">Radius of the sphere.</param>
    /// <returns>The readiness.</returns>
    public PlanetTerrainReadiness GetReadiness(PlanetId planet, Vector3 localCenter, float radius)
    {
        if (!_bodies.TryGetValue(planet.Value, out var body) || body.Node is null)
        {
            return PlanetTerrainReadiness.Inactive;
        }

        var clear = body.Planet.RadiusMeters + PlanetGeneratorV1.MaxReliefMeters + ClearMarginMeters;
        var distance = localCenter.Length();
        if (!float.IsFinite(distance) || !float.IsFinite(radius) || distance - radius > clear || distance < 1f)
        {
            return _service.GetReadiness(planet, localCenter, radius, clear);
        }

        var ground = body.GroundRadius(localCenter);
        var core = MathF.Min(MathF.Max(radius, 0f), GroundCoreMeters);
        if (ground < distance - radius - core || ground > distance + radius + core)
        {
            // No ground in the sphere: only a stream fault can make it not ready.
            return _service.GetReadiness(planet, localCenter, radius, float.NegativeInfinity);
        }

        return _service.GetReadiness(planet, localCenter / distance * ground, core, clear);
    }

    /// <summary>Radius of the generated ground of a planet along a direction (cached for the last direction; main thread).</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="direction">The direction in the planet frame.</param>
    /// <param name="radius">The radius.</param>
    /// <returns>False for an unknown planet.</returns>
    public bool TryGetGroundRadius(PlanetId planet, Vector3 direction, out float radius)
    {
        radius = _bodies.TryGetValue(planet.Value, out var body) ? body.GroundRadius(direction) : 0f;
        return body is not null;
    }

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Step(runtime.State.CurrentTick);
    }

    /// <summary>One step of the policy: activates and deactivates the terrains it lists (no-op until the frame graph exists).</summary>
    /// <param name="tick">The current tick.</param>
    public void Step(Tick tick)
    {
        if (Graph() is not { } graph)
        {
            return;
        }

        Policy.Update(tick, _state, graph, ShipDemand ?? NoShipDemand.Instance);
        var activate = Policy.Activate;
        for (var i = 0; i < activate.Count; i++)
        {
            Activate(activate[i]);
        }

        var deactivate = Policy.Deactivate;
        for (var i = 0; i < deactivate.Count; i++)
        {
            Deactivate(deactivate[i]);
        }

        if (History is { } history && tick.Value % ServerShipViewers.UpdateIntervalTicks == 0)
        {
            ShipViewers.Update(_state, history, TickSeconds, this);
        }
    }

    /// <summary>Writes the over-budget log entry of every active terrain (decision 7).</summary>
    /// <param name="governor">The governor (logger and tick average).</param>
    /// <param name="players">Players whose body is on a planet.</param>
    public void LogTerrainStats(TerrainTickGovernor governor, Func<PlanetId, int> players)
    {
        ArgumentNullException.ThrowIfNull(governor);
        ArgumentNullException.ThrowIfNull(players);
        foreach (var body in _bodies.Values)
        {
            if (body.Node is not { } node || !GodotObject.IsInstanceValid(node))
            {
                continue;
            }

            var terrain = node.Terrain;
            var statistics = terrain.GetStatistics();

            // time_update_task: microseconds of the last update task of Voxel Tools (ProfilingClock).
            var update = statistics.TryGetValue("time_update_task", out var micro) ? micro.AsDouble() / 1000.0 : 0.0;
            var count = players(body.Planet.Id);
            governor.LogTerrain(new PlanetTerrainStats(
                body.Planet.Id, count, count + ShipViewers.CountOn(body.Planet.Id), terrain.DebugGetDataBlockCount(), update));
        }
    }

    /// <summary>Takes every terrain node out of the tree and frees it (shutdown, after the terrain service closed the streams).</summary>
    public void FreeAll()
    {
        ShipViewers.Clear();
        foreach (var body in _bodies.Values)
        {
            if (body.Node is { } node && GodotObject.IsInstanceValid(node))
            {
                node.GetParent()?.RemoveChild(node);
                node.QueueFree();
            }

            body.Node = null;
        }
    }

    /// <summary>The generator of a planet (made at start for every body, active or not).</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="generator">The generator.</param>
    /// <returns>False for an unknown planet.</returns>
    public bool TryGetGenerator(PlanetId planet, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out VoxelGenerator? generator)
    {
        generator = _bodies.TryGetValue(planet.Value, out var body) ? body.Generator : null;
        return generator is not null;
    }

    internal void Release(PlanetTerrainLease lease)
    {
        Policy.ReleaseLease(lease.Planet, _state.CurrentTick);
        LogLeaseReleased(_logger, lease.Planet.Value, lease.Reason);
    }

    private void Activate(PlanetId planet)
    {
        if (!_bodies.TryGetValue(planet.Value, out var body) || body.Node is not null)
        {
            return;
        }

        var configured = _create(body.Planet, body.Generator);
        var node = _service.AddPlanet(body.Planet, configured, body.Mipmaps);
        Created?.Invoke(node);
        if (FrameNodes is not { } frames || !frames.Attach(node, FrameRef.Of(planet)))
        {
            // Planets do not move in EA: the placement of the planet is where its frame node will be.
            node.Position = _placement.TryGetPose(planet, out var pose) ? GodotFrames.ToGodot(pose.Position) : Godot.Vector3.Zero;
            _fallbackParent.AddChild(node);
        }

        body.Node = node;
        LogActivated(_logger, planet.Value, ActiveCount);
    }

    private void Deactivate(PlanetId planet)
    {
        if (!_bodies.TryGetValue(planet.Value, out var body) || body.Node is null)
        {
            return;
        }

        if (_replays > 0 || Policy.LeaseCount(planet) > 0 || planet == _state.HomePlanet)
        {
            LogDeactivationRefused(_logger, planet.Value, _replays > 0, Policy.LeaseCount(planet));
            return;
        }

        ShipViewers.RemovePlanet(planet);
        _service.RemovePlanet(planet);
        body.Node = null;
        LogDeactivated(_logger, planet.Value, ActiveCount);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Planet {Planet} terrain activated ({Active} active).")]
    private static partial void LogActivated(ILogger logger, ulong planet, int active);

    [LoggerMessage(Level = LogLevel.Information, Message = "Planet {Planet} terrain deactivated ({Active} active).")]
    private static partial void LogDeactivated(ILogger logger, ulong planet, int active);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Planet {Planet} terrain deactivation refused (replay {Replay}, leases {Leases}).")]
    private static partial void LogDeactivationRefused(ILogger logger, ulong planet, bool replay, int leases);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Planet {Planet} terrain lease acquired ({Reason}).")]
    private static partial void LogLeaseAcquired(ILogger logger, ulong planet, PlanetLeaseReason reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Planet {Planet} terrain lease released ({Reason}).")]
    private static partial void LogLeaseReleased(ILogger logger, ulong planet, PlanetLeaseReason reason);

    private sealed class Body(PlanetParams planet, VoxelGenerator generator, DerivedLodRebuilder? mipmaps, PlanetSurfaceSampler sampler)
    {
        /// <summary>Cosine of the angle under which a cached ground radius is reused (about 0.5 m at R = 4 km).</summary>
        private const float SameDirectionCos = 0.99999998f;

        private Vector3 _lastDirection;
        private float _lastGround = float.NaN;

        public PlanetSurfaceSampler Sampler { get; } = sampler;

        public float GroundRadius(Vector3 direction)
        {
            var length = direction.Length();
            var unit = length > 1e-6f ? direction / length : Vector3.UnitY;
            if (!float.IsNaN(_lastGround) && Vector3.Dot(unit, _lastDirection) >= SameDirectionCos)
            {
                return _lastGround;
            }

            _lastDirection = unit;
            _lastGround = Sampler.SurfaceRadius(unit);
            return _lastGround;
        }

        public PlanetParams Planet { get; } = planet;

        public VoxelGenerator Generator { get; } = generator;

        public DerivedLodRebuilder? Mipmaps { get; } = mipmaps;

        public PlanetTerrain? Node { get; set; }
    }

    private sealed class ReplayScope(ServerPlanetTerrains owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner._replays--;
            }
        }
    }

    private sealed class NoShipDemand : IShipDemandSource
    {
        public static readonly NoShipDemand Instance = new();

        public bool Demands(Horyzont.Core.World.Constructs.Construct ship) => false;
    }
}
