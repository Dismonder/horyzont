using Godot;

using Horyzont.Client;
using Horyzont.Client.Replication;
using Horyzont.Game.Frames;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Client.Replication;

/// <summary>
/// Draws the visual overlay of the local player's not yet confirmed block edits (K3-15 decision 10): a translucent green cube where a
/// block was placed, a red one where one was removed. It owns no physics body and no collision layer, and it never changes the
/// authoritative copy; an entry disappears with the operation, a matching full state, a failed intent or the 300-tick deadline.
/// </summary>
public sealed partial class PredictedBlocksView : Node3D
{
    private readonly List<MeshInstance3D> _boxes = [];
    private ConstructPrediction? _prediction;
    private ConstructMirror? _mirror;
    private ILocalPlayerView? _player;
    private Mesh? _mesh;
    private Material? _place;
    private Material? _remove;
    private bool _dirty = true;

    /// <summary>Gets the number of cubes currently drawn.</summary>
    public int BoxCount => _boxes.Count;

    /// <summary>Gives the node its dependencies; call before it enters the tree.</summary>
    /// <param name="prediction">The overlay to draw.</param>
    /// <param name="mirror">Mirror whose construct poses place the cubes.</param>
    /// <param name="player">Local player (the planet of the frame).</param>
    public void Initialize(ConstructPrediction prediction, ConstructMirror mirror, ILocalPlayerView player)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        ArgumentNullException.ThrowIfNull(mirror);
        ArgumentNullException.ThrowIfNull(player);
        _prediction = prediction;
        _mirror = mirror;
        _player = player;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        if (_prediction is null)
        {
            throw new InvalidOperationException("PredictedBlocksView.Initialize was not called before the node entered the tree.");
        }

        _mesh = new BoxMesh { Size = Vector3.One * 1.002f };
        _place = Translucent(new Color(0.2f, 0.9f, 0.3f, 0.45f));
        _remove = Translucent(new Color(0.95f, 0.2f, 0.2f, 0.45f));
        _prediction.OverlayChanged += OnChanged;
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_prediction is not null)
        {
            _prediction.OverlayChanged -= OnChanged;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // Entries also expire with the clock, so the list is read every frame; cubes are rebuilt only when it changed.
        var entries = _prediction!.Entries;
        if (!_dirty && entries.Count == _boxes.Count)
        {
            return;
        }

        _dirty = false;
        while (_boxes.Count > entries.Count)
        {
            _boxes[^1].QueueFree();
            _boxes.RemoveAt(_boxes.Count - 1);
        }

        while (_boxes.Count < entries.Count)
        {
            var box = new MeshInstance3D { Mesh = _mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(box);
            _boxes.Add(box);
        }

        var planet = _player!.Planet;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            NumVector3 origin;
            System.Numerics.Quaternion rotation;
            if (entry.IsNewConstruct)
            {
                origin = entry.Origin;
                rotation = entry.Rotation;
            }
            else if (_mirror!.TryGetPose(entry.Construct, out var pose))
            {
                origin = pose.Origin;
                rotation = pose.Rotation;
            }
            else
            {
                _boxes[i].Visible = false;
                continue;
            }

            var center = origin + NumVector3.Transform(new NumVector3(entry.Cell.X + 0.5f, entry.Cell.Y + 0.5f, entry.Cell.Z + 0.5f), rotation);
            _boxes[i].Visible = true;
            _boxes[i].MaterialOverride = entry.Block.IsEmpty ? _remove : _place;
            _boxes[i].Position = PlanetFrames.ToGodot(planet, center);
            _boxes[i].Basis = PlanetFrames.ToGodotBasis(planet, rotation);
        }
    }

    private static StandardMaterial3D Translucent(Color color) => new()
    {
        AlbedoColor = color,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    private void OnChanged() => _dirty = true;
}
