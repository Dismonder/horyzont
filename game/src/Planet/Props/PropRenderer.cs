using Godot;

using Horyzont.Client.Props;
using Horyzont.Core.Ids;
using Horyzont.Core.Props;
using Horyzont.Core.World;
using Horyzont.Game.Client.Terrain;
using Horyzont.Game.Frames;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Planet.Rendering;
using Horyzont.Game.Planet.Surface;
using Horyzont.Server.Props;

using Microsoft.Extensions.Logging;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Planet.Props;

/// <summary>
/// Draws the props of the layout V1 around the camera (K6-31 D20): the trees, bushes, rocks, boulders and crystal clusters of
/// <c>flora.glb</c> as one <see cref="MultiMeshInstance3D"/> per region and kind, minus the harvested ones. The anchors of a region are
/// found on a worker thread (<see cref="PropRegionAnchors"/>, the same surface call as the server), the nodes are built on the main
/// thread. Regions within <see cref="RenderRadiusMeters"/> of the camera are drawn, those beyond <see cref="DropRadiusMeters"/> are freed.
/// A change of the harvested props rebuilds only the region it belongs to. The node is also the <see cref="IPropSource"/> of the picker:
/// the anchors it knows are what the player sees. Main thread; only the planet of the camera has props.
/// </summary>
public sealed partial class PropRenderer : Node, IPropSource
{
    /// <summary>Regions whose centre is this close to the camera are drawn, in metres.</summary>
    public const float RenderRadiusMeters = 384f;

    /// <summary>Regions farther than this from the camera are freed, in metres.</summary>
    public const float DropRadiusMeters = 448f;

    /// <summary>Anchor jobs in flight at once.</summary>
    public const int MaxAnchorJobs = 8;

    /// <summary>Region nodes built (or rebuilt) in one step.</summary>
    public const int MaxBuildsPerStep = 6;

    // A remote client forgets the harvested props of far regions about once a second (the state outlives them by 768 m).
    private const int ForgetEverySteps = 60;

    // Values of PropKindV1 run 1-8.
    private const int KindCount = 9;

    private readonly Dictionary<ulong, Body> _bodies = [];
    private readonly List<PropRegionKey> _wanted = [];
    private readonly HashSet<ulong> _wantedSet = [];
    private readonly List<ulong> _scratch = [];
    private readonly PropInterest _interest = new();
    private Func<IReadOnlyList<PlanetParams>?> _planets = static () => null;
    private Func<FrameNodes?> _nodes = static () => null;
    private Func<ClientViewpoint> _viewpoint = static () => default;
    private Func<PlanetParams, PlanetSurfaceSampler> _samplers = static planet => new PlanetSurfaceSampler(planet, GeneratorProfiles.CreateGenerator(planet));
    private Func<IReadOnlyDictionary<string, Mesh>> _loadMeshes = static () => PlanetFlora.LoadMeshes();
    private IReadOnlyDictionary<string, Mesh>? _meshes;
    private IPropStateView? _states;
    private ILogger _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    private int _jobs;
    private int _steps;

    /// <summary>Gets the number of anchor jobs that run or wait for their result to be taken.</summary>
    public int PendingJobs => _jobs;

    /// <summary>Wires the node.</summary>
    /// <param name="planets">The planets of the star system, or null while they are not known.</param>
    /// <param name="nodes">The client frame nodes (the props hang under the frame of their planet); null = under this node.</param>
    /// <param name="viewpoint">Where the camera is.</param>
    /// <param name="states">The harvested props.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="samplers">Gives the surface sampler of a planet; by default one made from its generator profile.</param>
    /// <param name="meshes">Loads the meshes by item name; by default those of <c>flora.glb</c>.</param>
    public void Initialize(
        Func<IReadOnlyList<PlanetParams>?> planets, Func<FrameNodes?> nodes, Func<ClientViewpoint> viewpoint, IPropStateView states, ILogger logger,
        Func<PlanetParams, PlanetSurfaceSampler>? samplers = null, Func<IReadOnlyDictionary<string, Mesh>>? meshes = null)
    {
        _planets = planets ?? throw new ArgumentNullException(nameof(planets));
        _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        _viewpoint = viewpoint ?? throw new ArgumentNullException(nameof(viewpoint));
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (samplers is not null)
        {
            _samplers = samplers;
        }

        if (meshes is not null)
        {
            _loadMeshes = meshes;
        }

        _states.RegionChanged += OnRegionChanged;
    }

    /// <summary>Gets the number of regions with a node built.</summary>
    public int BuiltRegions
    {
        get
        {
            var count = 0;
            foreach (var body in _bodies.Values)
            {
                foreach (var region in body.Regions.Values)
                {
                    count += region.Node is not null ? 1 : 0;
                }
            }

            return count;
        }
    }

    /// <summary>Tells how many props a region draws (the anchors minus the harvested ones).</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="region">The region.</param>
    /// <returns>The instance count over all kinds; -1 while the region has no node.</returns>
    public int InstanceCount(PlanetId planet, PropRegionKey region) =>
        _bodies.TryGetValue(planet.Value, out var body) && body.Regions.TryGetValue(region.Value, out var state) && state.Node is not null ? state.Instances : -1;

    /// <summary>Tells how many times the node of a region was built since the region came into view.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="region">The region.</param>
    /// <returns>The number of builds; 0 for an unknown region.</returns>
    public int Builds(PlanetId planet, PropRegionKey region) =>
        _bodies.TryGetValue(planet.Value, out var body) && body.Regions.TryGetValue(region.Value, out var state) ? state.Builds : 0;

    /// <summary>Gets the node of a region, or null.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="region">The region.</param>
    /// <returns>The node, whose children are the multimesh instances of the kinds.</returns>
    public Node3D? RegionNode(PlanetId planet, PropRegionKey region) =>
        _bodies.TryGetValue(planet.Value, out var body) && body.Regions.TryGetValue(region.Value, out var state) && state.Node is { } node && IsInstanceValid(node)
            ? node
            : null;

    /// <inheritdoc />
    public void CollectInSphere(PlanetId planet, NumVector3 center, float radius, List<PropInstance> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!_bodies.TryGetValue(planet.Value, out var body))
        {
            return;
        }

        foreach (var region in body.Regions.Values)
        {
            if (region.Visuals is not { } visuals || !PropRegionReach.MayHold(region.Centre, center, radius))
            {
                continue;
            }

            foreach (var visual in visuals)
            {
                if (NumVector3.DistanceSquared(visual.Instance.Anchor, center) <= radius * radius)
                {
                    result.Add(visual.Instance);
                }
            }
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta) => Step();

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_states is not null)
        {
            _states.RegionChanged -= OnRegionChanged;
        }
    }

    /// <summary>Takes finished anchor jobs, starts missing ones, frees far regions and builds the nodes that are due.</summary>
    public void Step()
    {
        var viewpoint = _viewpoint();
        var planets = _planets();
        if (_states is null || !viewpoint.HasBody || planets is null)
        {
            return;
        }

        PlanetParams? planet = null;
        foreach (var candidate in planets)
        {
            if (candidate.Id == viewpoint.Planet)
            {
                planet = candidate;
                break;
            }
        }

        if (planet is null || !PropLayouts.TryGet(planet.GeneratorProfile, out var layout) || !CubeSphereGrid.TryGetCellsPerFace(planet.RadiusMeters, out var cells))
        {
            return;
        }

        var body = BodyOf(planet, layout, cells);
        if (body is null)
        {
            return;
        }

        // Props of other planets are not needed once the camera left them.
        _scratch.Clear();
        foreach (var other in _bodies)
        {
            if (other.Key != planet.Id.Value)
            {
                _scratch.Add(other.Key);
            }
        }

        foreach (var key in _scratch)
        {
            FreeBody(_bodies[key]);
            _bodies.Remove(key);
        }

        TakeFinished(body);
        if (_states is ClientPropStates remote && ++_steps % ForgetEverySteps == 0)
        {
            remote.Forget(planet, viewpoint.Camera);
        }

        _interest.CollectRegions(planet, viewpoint.Camera, RenderRadiusMeters, _wanted);
        _wantedSet.Clear();
        foreach (var region in _wanted)
        {
            _wantedSet.Add(region.Value);
            if (!body.Regions.ContainsKey(region.Value) && _jobs < MaxAnchorJobs)
            {
                Start(body, region);
            }
        }

        Drop(body, planet, viewpoint.Camera);
        Build(body);
    }

    private static float SinkMeters(PropKindV1 kind) => kind switch
    {
        // The meshes sit on their anchor; the instancer sank them a little into the ground so that slopes show no gap (see PlanetFlora).
        PropKindV1.Pine or PropKindV1.PineTall or PropKindV1.Broadleaf or PropKindV1.BroadleafAutumn or PropKindV1.Crystals => -0.2f,
        PropKindV1.Rock => -0.15f,
        PropKindV1.Boulder => -0.6f,
        _ => 0f,
    };

    private Body? BodyOf(PlanetParams planet, IPropLayout layout, int cells)
    {
        if (_bodies.TryGetValue(planet.Id.Value, out var existing))
        {
            return existing;
        }

        var root = new Node3D { Name = $"Props_{planet.Id.Value}" };
        var attached = _nodes() is { } nodes && nodes.Attach(root, FrameRef.Of(planet.Id));
        if (!attached)
        {
            AddChild(root);
        }

        var body = new Body(planet, layout, cells, _samplers(planet), root);
        _bodies.Add(planet.Id.Value, body);
        return body;
    }

    private void Start(Body body, PropRegionKey key)
    {
        var region = new Region(key, PropRegionKeyCentre(body, key));
        body.Regions.Add(key.Value, region);
        _jobs++;
        var planet = body.Planet;
        var layout = body.Layout;
        var sampler = body.Sampler;
        region.Job = Task.Run(() => PropRegionAnchors.Compute(planet, layout, sampler, key));
    }

    private static NumVector3 PropRegionKeyCentre(Body body, PropRegionKey key) =>
        PropRegionAnchors.CentreDirection(key, body.Cells) * body.Planet.RadiusMeters;

    private void TakeFinished(Body body)
    {
        foreach (var region in body.Regions.Values)
        {
            if (region.Job is not { IsCompleted: true } job)
            {
                continue;
            }

            region.Job = null;
            _jobs--;
            if (job.IsCompletedSuccessfully)
            {
                region.Visuals = job.Result;
                region.Dirty = true;
            }
            else
            {
                region.Failed = true;
                PropRendererLog.AnchorsFailed(_logger, job.Exception, body.Planet.Id.Value, region.Key.Value);
            }
        }
    }

    private void Drop(Body body, PlanetParams planet, NumVector3 camera)
    {
        _scratch.Clear();
        foreach (var region in body.Regions.Values)
        {
            if (_wantedSet.Contains(region.Key.Value))
            {
                continue;
            }

            if (PropInterest.DistanceToRegion(planet, camera, region.Key) > DropRadiusMeters)
            {
                _scratch.Add(region.Key.Value);
            }
        }

        foreach (var key in _scratch)
        {
            var region = body.Regions[key];
            region.Free();
            if (region.Job is not null)
            {
                _jobs--; // the result is simply never taken
            }

            body.Regions.Remove(key);
        }
    }

    private void Build(Body body)
    {
        var built = 0;
        foreach (var region in body.Regions.Values)
        {
            if (!region.Dirty || region.Visuals is null)
            {
                continue;
            }

            if (built++ >= MaxBuildsPerStep)
            {
                break;
            }

            Rebuild(body, region);
        }
    }

    private void Rebuild(Body body, Region region)
    {
        region.Dirty = false;
        region.Free();
        _meshes ??= _loadMeshes();
        var visuals = region.Visuals!;
        var counts = new int[KindCount];
        foreach (var visual in visuals)
        {
            if (!_states!.IsHarvested(body.Planet.Id, visual.Instance.Id))
            {
                counts[(int)visual.Instance.Kind]++;
            }
        }

        var node = new Node3D { Name = $"Region_{region.Key.Value}", Position = new Vector3(region.Centre.X, region.Centre.Y, region.Centre.Z) };
        var next = new int[counts.Length];
        var multis = new MultiMesh?[counts.Length];
        var total = 0;
        foreach (var visual in visuals)
        {
            var kind = (int)visual.Instance.Kind;
            if (counts[kind] == 0 || _states!.IsHarvested(body.Planet.Id, visual.Instance.Id) || !_meshes.TryGetValue(visual.Instance.Kind.ToString(), out var mesh))
            {
                continue;
            }

            var multi = multis[kind];
            if (multi is null)
            {
                multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = counts[kind] };
                multis[kind] = multi;
            }

            multi.SetInstanceTransform(next[kind]++, TransformOf(visual, region.Centre));
            total++;
        }

        for (var kind = 0; kind < multis.Length; kind++)
        {
            if (multis[kind] is { } multi)
            {
                multi.VisibleInstanceCount = next[kind];
                node.AddChild(new MultiMeshInstance3D { Name = ((PropKindV1)kind).ToString(), Multimesh = multi });
            }
        }

        body.Root.AddChild(node);
        region.Node = node;
        region.Instances = total;
        region.Builds++;
    }

    private static Transform3D TransformOf(PropVisual visual, NumVector3 centre)
    {
        var instance = visual.Instance;
        var up = new Vector3(instance.Up.X, instance.Up.Y, instance.Up.Z).Normalized();
        var reference = Mathf.Abs(up.Y) < 0.99 ? Vector3.Up : Vector3.Right;
        var east = reference.Cross(up).Normalized();
        var north = up.Cross(east);
        var cos = Mathf.Cos(visual.YawRadians);
        var sin = Mathf.Sin(visual.YawRadians);
        var x = (east * cos) + (north * sin);
        var z = x.Cross(up);
        var scale = instance.Scale;
        var origin = new Vector3(instance.Anchor.X - centre.X, instance.Anchor.Y - centre.Y, instance.Anchor.Z - centre.Z) + (up * SinkMeters(instance.Kind));
        return new Transform3D(new Basis(x * scale, up * scale, z * scale), origin);
    }

    private void OnRegionChanged(PlanetId planet, PropRegionKey region)
    {
        if (_bodies.TryGetValue(planet.Value, out var body) && body.Regions.TryGetValue(region.Value, out var state))
        {
            state.Dirty = true;
        }
    }

    private void FreeBody(Body body)
    {
        foreach (var region in body.Regions.Values)
        {
            region.Free();

            // A job nobody took is settled with its region: its result is never taken once the body is gone, and a job that stayed counted
            // would hold one of the MaxAnchorJobs slots for good (eight such jobs would stop every later planet from getting props).
            if (region.Job is not null)
            {
                region.Job = null;
                _jobs--;
            }
        }

        if (IsInstanceValid(body.Root))
        {
            body.Root.GetParent()?.RemoveChild(body.Root);
            body.Root.QueueFree();
        }
    }

    private sealed class Body(PlanetParams planet, IPropLayout layout, int cells, PlanetSurfaceSampler sampler, Node3D root)
    {
        public PlanetParams Planet { get; } = planet;

        public IPropLayout Layout { get; } = layout;

        public int Cells { get; } = cells;

        public PlanetSurfaceSampler Sampler { get; } = sampler;

        public Node3D Root { get; } = root;

        public Dictionary<ulong, Region> Regions { get; } = [];
    }

    private sealed class Region(PropRegionKey key, NumVector3 centre)
    {
        public PropRegionKey Key { get; } = key;

        public NumVector3 Centre { get; } = centre;

        public Task<PropVisual[]>? Job { get; set; }

        public PropVisual[]? Visuals { get; set; }

        public Node3D? Node { get; set; }

        public bool Dirty { get; set; }

        public bool Failed { get; set; }

        public int Instances { get; set; }

        public int Builds { get; set; }

        public void Free()
        {
            if (Node is { } node && IsInstanceValid(node))
            {
                node.GetParent()?.RemoveChild(node);
                node.QueueFree();
            }

            Node = null;
            Instances = 0;
        }
    }
}

internal static partial class PropRendererLog
{
    [LoggerMessage(EventId = 9511, Level = LogLevel.Warning, Message = "Prop anchors of region {Region} of planet {Planet} failed")]
    internal static partial void AnchorsFailed(ILogger logger, Exception? exception, ulong planet, ulong region);
}
