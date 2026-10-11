using Godot;

using Horyzont.Client;
using Horyzont.Client.Space;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Space;
using Horyzont.Game.Server.Characters;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Client.Terrain;

/// <summary>
/// The client side of terrain activation (K5-22 decision 10): every frame <see cref="ClientTerrainSelection"/> picks the active body
/// (the region planet of the own body is forced; otherwise the nearest body by <c>r/R</c> from 4 R, released over 4.4 R, one fading for
/// 10 s) and the terrain provider is told to <see cref="IClientTerrainProvider.Activate"/> / <see cref="IClientTerrainProvider.Release"/>
/// (a remote client creates and frees nodes; a host takes and drops a lease on the server terrain). The terrain viewers of the client
/// live here (moved out of <c>LocalPlayer</c>): the visual viewer at the camera on the active body (rendering only) and, on a remote
/// client, the collision viewer at the feet on the planet of the body (a host shares the collisions of the server). As
/// <see cref="ITerrainViewerSource"/> it reports the view of the active body, so <c>TerrainView</c> follows it. Main thread.
/// </summary>
public sealed partial class ClientPlanetTerrains : Node, ITerrainViewerSource
{
    private readonly ClientTerrainSelection _selection = new();
    private readonly SortedSet<ulong> _wanted = [];
    private readonly List<ulong> _drop = [];
    private readonly Dictionary<ulong, float> _weights = [];
    private readonly ViewerSlot _visual = new("CameraTerrainViewer", visuals: true);
    private readonly ViewerSlot _collision = new("BodyCollisionViewer", visuals: false);
    private BodyInfo[] _bodies = [];
    private Func<IClientTerrainProvider?> _provider = static () => null;
    private Func<ClientStarSystem?> _system = static () => null;
    private Func<ClientViewpoint> _viewpoint = static () => default;
    private Func<bool> _collisions = static () => false;
    private bool _render;
    private PlanetId _viewPlanet;
    private NumVector3 _viewFeet;
    private bool _hasView;

    /// <summary>
    /// Gets or sets whether the impostor of a body is placed (decision 13): without it the terrain of the body is drawn whole (weight 0).
    /// </summary>
    public Func<PlanetId, bool> ImpostorReady { get; set; } = static _ => false;

    /// <summary>The impostor share of a body for this frame (decision 13), quantized; 1 for a body without an active terrain.</summary>
    /// <param name="planet">The body.</param>
    /// <returns>The weight in [0, 1].</returns>
    public float ImpostorWeight(PlanetId planet) => _weights.TryGetValue(planet.Value, out var weight) ? weight : 1f;

    /// <summary>Gets the selection (active and fading body).</summary>
    public ClientTerrainSelection Selection => _selection;

    /// <summary>Gets the viewer that streams the visible terrain around the camera; null without rendering or an active body.</summary>
    public VoxelViewer? VisualViewer => _visual.Live;

    /// <summary>Gets the collision viewer at the feet of the own body (remote client only); null otherwise.</summary>
    public VoxelViewer? CollisionViewer => _collision.Live;

    /// <summary>Wires the controller.</summary>
    /// <param name="provider">The current terrain provider of the client.</param>
    /// <param name="system">The star system of the client; null before it exists.</param>
    /// <param name="viewpoint">The own body and the camera in the frame of the body planet.</param>
    /// <param name="render">Whether the process renders (the camera viewer needs visuals).</param>
    /// <param name="collisions">Whether the client builds its own collisions (a remote client; a host shares the server ones).</param>
    public void Initialize(
        Func<IClientTerrainProvider?> provider, Func<ClientStarSystem?> system, Func<ClientViewpoint> viewpoint, bool render, Func<bool> collisions)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _system = system ?? throw new ArgumentNullException(nameof(system));
        _viewpoint = viewpoint ?? throw new ArgumentNullException(nameof(viewpoint));
        _collisions = collisions ?? throw new ArgumentNullException(nameof(collisions));
        _render = render;
    }

    /// <inheritdoc />
    public override void _Process(double delta) => Step(Time.GetTicksMsec() / 1000.0);

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _visual.Remove();
        _collision.Remove();
    }

    /// <inheritdoc />
    /// <remarks>
    /// The view is the one of the active body: the visual viewer when it renders, and the feet of the own body expressed in the frame of
    /// that body as the collision position (the server checks it against the feet in the same frame), with the collision distance only
    /// when the collision viewer stands on that body.
    /// </remarks>
    public bool TryGetViewers(out TerrainViewers viewers)
    {
        viewers = default;
        if (!_hasView)
        {
            return false;
        }

        var visual = _visual.Live is not null && _visual.Planet == _viewPlanet;
        var collision = _collision.Live is not null && _collision.Planet == _viewPlanet;
        if (!visual && !collision)
        {
            return false;
        }

        viewers = new TerrainViewers(
            _viewPlanet,
            visual ? _visual.Local : default, visual ? _visual.Distance : 0,
            collision ? _collision.Local : _viewFeet, collision ? _collision.Distance : 0);
        return true;
    }

    /// <summary>One selection step.</summary>
    /// <param name="nowSeconds">Render time in seconds.</param>
    public void Step(double nowSeconds)
    {
        if (_provider() is not { } provider)
        {
            return;
        }

        var viewpoint = _viewpoint();
        var forced = viewpoint.Forced;
        var count = FillBodies();
        var known = TryCameraInSystem(viewpoint, out var camera);
        if (known || forced != default)
        {
            _selection.Update(known ? camera : default, _bodies.AsSpan(0, count), forced, nowSeconds);
        }

        _drop.Clear();
        foreach (var planet in _wanted)
        {
            if (planet != _selection.Active.Value && planet != _selection.Fading.Value && planet != forced.Value)
            {
                _drop.Add(planet);
            }
        }

        foreach (var planet in _drop)
        {
            _wanted.Remove(planet);
            _weights.Remove(planet);
            _visual.RemoveIfOn(new PlanetId(planet));
            _collision.RemoveIfOn(new PlanetId(planet));
            provider.Release(new PlanetId(planet));
        }

        Want(provider, forced);
        Want(provider, _selection.Active);
        Want(provider, _selection.Fading);
        UpdateViewers(provider, viewpoint, known);
        UpdateBlend(provider, viewpoint);
    }

    /// <summary>Decision 13: the cross-fade weight of every wanted body from the camera altitude over it, applied to its terrain.</summary>
    private void UpdateBlend(IClientTerrainProvider provider, in ClientViewpoint viewpoint)
    {
        foreach (var planet in _wanted)
        {
            var id = new PlanetId(planet);
            var weight = 0f;
            if (_render && ImpostorReady(id) && viewpoint.HasBody && TryToBody(viewpoint, id, viewpoint.Camera, out var eye, out var radius))
            {
                // ±0.25 level of hysteresis: a camera at a level boundary does not swap the terrain material every frame.
                var previous = _weights.TryGetValue(planet, out var held) ? held : float.NaN;
                weight = ImpostorBlend.QuantizeWithHysteresis(ImpostorBlend.ImpostorWeight(eye.Length() - radius, radius), previous);
            }

            _weights[planet] = weight;
            if (provider.TryGetPlanet(id, out var view))
            {
                view.SetImpostorBlend(weight);
            }
        }
    }

    private void Want(IClientTerrainProvider provider, PlanetId planet)
    {
        if (planet != default && provider.Activate(planet))
        {
            _wanted.Add(planet.Value);
        }
    }

    private int FillBodies()
    {
        if (_system() is { Model: { } model })
        {
            if (_bodies.Length < model.BodyCount)
            {
                _bodies = new BodyInfo[model.BodyCount];
            }

            for (var i = 0; i < model.BodyCount; i++)
            {
                _bodies[i] = model.GetBody(i);
            }

            return model.BodyCount;
        }

        // Before the star system state only the home planet (or the own planet) is known; the forced planet decides alone.
        return 0;
    }

    private bool TryCameraInSystem(in ClientViewpoint viewpoint, out Vector3D camera)
    {
        if (viewpoint.HasBody && _system() is { } system && system.TryGetPose(viewpoint.Planet, out var pose))
        {
            camera = pose.Position + Vector3D.FromFloat(viewpoint.Camera);
            return true;
        }

        camera = default;
        return false;
    }

    /// <summary>Expresses a point of the body planet frame in the frame of another body (planets do not rotate, K5-22 scope).</summary>
    private bool TryToBody(in ClientViewpoint viewpoint, PlanetId planet, NumVector3 local, out NumVector3 result, out double radius)
    {
        if (planet == viewpoint.Planet)
        {
            result = local;
            radius = viewpoint.PlanetRadius;
            return true;
        }

        if (_system() is { Model: { } model } system && model.TryGetBody(planet, out var body)
            && system.TryGetPose(viewpoint.Planet, out var from))
        {
            var offset = from.Position + Vector3D.FromFloat(local) - body.Position;
            result = new NumVector3((float)offset.X, (float)offset.Y, (float)offset.Z);
            radius = body.RadiusMeters;
            return true;
        }

        result = default;
        radius = 0;
        return false;
    }

    private void UpdateViewers(IClientTerrainProvider provider, in ClientViewpoint viewpoint, bool known)
    {
        var active = _selection.Active != default ? _selection.Active : viewpoint.Forced;
        _hasView = false;
        if (!viewpoint.HasBody || active == default || !TryToBody(viewpoint, active, viewpoint.Feet, out _viewFeet, out _))
        {
            _visual.Remove();
            _collision.Remove();
            return;
        }

        _viewPlanet = active;
        _hasView = true;
        if (_render && (known || active == viewpoint.Planet) && TryToBody(viewpoint, active, viewpoint.Camera, out var eye, out var radius)
            && provider.TryGetPlanet(active, out var view))
        {
            _visual.Place(view, active, eye, ViewDistanceFor(eye.Length() - radius));
        }
        else
        {
            _visual.Remove();
        }

        if (_collisions() && provider.TryGetPlanet(viewpoint.Planet, out var own))
        {
            _collision.Place(own, viewpoint.Planet, viewpoint.Feet, (uint)ServerCharacters.ServerViewerDistance);
        }
        else
        {
            _collision.Remove();
        }
    }

    /// <summary>The visual view distance by the altitude of the camera over the body (up to 3 R it reaches 16 384 m).</summary>
    /// <param name="altitude">Altitude in metres.</param>
    /// <returns>The view distance in metres.</returns>
    internal static uint ViewDistanceFor(double altitude) => altitude switch
    {
        < 300 => 2048u,
        < 1500 => 4096u,
        < 3000 => 8192u,
        _ => 16384u,
    };

    /// <summary>One viewer under the client anchor of a planet terrain; the position is planet-local (the anchor stands at the planet centre).</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design", "CA1001:Types that own disposable fields should be disposable",
        Justification = "The viewer is a Godot node in the scene tree, freed with QueueFree by Remove.")]
    private sealed class ViewerSlot(string name, bool visuals)
    {
        private VoxelViewer? _viewer;

        public PlanetId Planet { get; private set; }

        public NumVector3 Local { get; private set; }

        public uint Distance { get; private set; }

        public VoxelViewer? Live => _viewer is { } viewer && IsInstanceValid(viewer) ? viewer : null;

        public void Place(IClientTerrainView view, PlanetId planet, NumVector3 local, uint distance)
        {
            if (Live is null || Planet != planet || _viewer!.GetParent() != view.Anchor)
            {
                Remove();
                _viewer = new VoxelViewer { Name = name, RequiresVisuals = visuals, RequiresCollisions = !visuals, ViewDistance = distance };
                view.AddViewer(_viewer);
                Planet = planet;
            }

            if (Distance != distance)
            {
                _viewer!.ViewDistance = distance;
            }

            Distance = distance;
            Local = local;
            _viewer!.Position = new Vector3(local.X, local.Y, local.Z);
        }

        public void RemoveIfOn(PlanetId planet)
        {
            if (Planet == planet)
            {
                Remove();
            }
        }

        /// <summary>Detaches and frees the viewer (a viewer freed with its terrain node is only forgotten).</summary>
        public void Remove()
        {
            if (_viewer is { } viewer && IsInstanceValid(viewer))
            {
                viewer.GetParent()?.RemoveChild(viewer);
                viewer.QueueFree();
            }

            _viewer = null;
            Planet = default;
            Distance = 0;
        }
    }
}
