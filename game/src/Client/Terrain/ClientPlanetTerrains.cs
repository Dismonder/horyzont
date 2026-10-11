using Godot;

using Horyzont.Client.Space;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Space;

namespace Horyzont.Game.Client.Terrain;

/// <summary>
/// The client side of terrain activation (K5-22 decision 10): every frame <see cref="ClientTerrainSelection"/> picks the active body
/// (the region planet of the own body is forced; otherwise the nearest body by <c>r/R</c> from 4 R, released over 4.4 R, one fading for
/// 10 s) and the terrain provider is told to <see cref="IClientTerrainProvider.Activate"/> / <see cref="IClientTerrainProvider.Release"/>
/// (a remote client creates and frees nodes; a host takes and drops a lease on the server terrain). While the active body is not the
/// planet of the own body, a visual viewer stands at the camera (the viewers of the body stay with <c>LocalPlayer</c>). Main thread.
/// </summary>
public sealed partial class ClientPlanetTerrains : Node
{
    private readonly ClientTerrainSelection _selection = new();
    private readonly SortedSet<ulong> _wanted = [];
    private readonly List<ulong> _drop = [];
    private BodyInfo[] _bodies = [];
    private Func<IClientTerrainProvider?> _provider = static () => null;
    private Func<ClientStarSystem?> _system = static () => null;
    private Func<PlanetId> _forced = static () => default;
    private Func<(bool Known, Vector3D Position)> _camera = static () => (false, default);
    private bool _render;
    private VoxelViewer? _cameraViewer;
    private PlanetId _cameraViewerPlanet;

    /// <summary>Gets the selection (active and fading body).</summary>
    public ClientTerrainSelection Selection => _selection;

    /// <summary>Wires the controller.</summary>
    /// <param name="provider">The current terrain provider of the client.</param>
    /// <param name="system">The star system of the client; null before it exists.</param>
    /// <param name="forced">The planet of the own body (forced active), or default.</param>
    /// <param name="camera">The camera position in the system frame (Godot world), when known.</param>
    /// <param name="render">Whether the process renders (the camera viewer needs visuals).</param>
    public void Initialize(
        Func<IClientTerrainProvider?> provider, Func<ClientStarSystem?> system, Func<PlanetId> forced, Func<(bool Known, Vector3D Position)> camera, bool render)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _system = system ?? throw new ArgumentNullException(nameof(system));
        _forced = forced ?? throw new ArgumentNullException(nameof(forced));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _render = render;
    }

    /// <inheritdoc />
    public override void _Process(double delta) => Step(Time.GetTicksMsec() / 1000.0);

    /// <inheritdoc />
    public override void _ExitTree() => RemoveCameraViewer();

    /// <summary>One selection step.</summary>
    /// <param name="nowSeconds">Render time in seconds.</param>
    public void Step(double nowSeconds)
    {
        if (_provider() is not { } provider)
        {
            return;
        }

        var forced = _forced();
        var count = FillBodies(forced);
        var (known, camera) = _camera();
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
            provider.Release(new PlanetId(planet));
        }

        Want(provider, forced);
        Want(provider, _selection.Active);
        Want(provider, _selection.Fading);
        UpdateCameraViewer(provider, forced, known, camera);
    }

    private void Want(IClientTerrainProvider provider, PlanetId planet)
    {
        if (planet != default && provider.Activate(planet))
        {
            _wanted.Add(planet.Value);
        }
    }

    private int FillBodies(PlanetId forced)
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

    private void UpdateCameraViewer(IClientTerrainProvider provider, PlanetId forced, bool known, Vector3D camera)
    {
        var active = _selection.Active;
        if (!_render || !known || active == default || active == forced || !provider.TryGetPlanet(active, out var view)
            || !TryGetBody(active, out var body))
        {
            RemoveCameraViewer();
            return;
        }

        if (_cameraViewer is null || _cameraViewerPlanet != active || !IsInstanceValid(_cameraViewer))
        {
            RemoveCameraViewer();
            _cameraViewer = new VoxelViewer { Name = "CameraTerrainViewer", RequiresVisuals = true, RequiresCollisions = false };
            view.AddViewer(_cameraViewer);
            _cameraViewerPlanet = active;
        }

        var altitude = Vector3D.Distance(camera, body.Position) - body.RadiusMeters;
        _cameraViewer.ViewDistance = altitude switch
        {
            < 300 => 2048u,
            < 1500 => 4096u,
            < 3000 => 8192u,
            _ => 16384u,
        };
        _cameraViewer.GlobalPosition = Horyzont.Game.Frames.GodotFrames.ToGodot(camera);
    }

    private bool TryGetBody(PlanetId planet, out BodyInfo body)
    {
        if (_system() is { Model: { } model })
        {
            return model.TryGetBody(planet, out body);
        }

        body = default;
        return false;
    }

    private void RemoveCameraViewer()
    {
        if (_cameraViewer is { } viewer && IsInstanceValid(viewer))
        {
            if (_provider() is { } provider && provider.TryGetPlanet(_cameraViewerPlanet, out var view))
            {
                view.RemoveViewer(viewer);
            }

            viewer.QueueFree();
        }

        _cameraViewer = null;
        _cameraViewerPlanet = default;
    }
}
