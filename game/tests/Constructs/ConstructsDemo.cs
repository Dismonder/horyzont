using Godot;

using Horyzont.Testing;

namespace Horyzont.Game.Tests.Constructs;

/// <summary>
/// Development scene (<c>res://scenes/dev/ConstructsDemo.tscn</c>): the Building fixture over a plane with a free-fly camera, for
/// judging the meshes by eye (winding, merged faces, prefab shapes). WASD/QE move, right mouse button looks, Shift is faster.
/// <c>-- --screenshot=path.png</c> saves the first stable frame and quits.
/// </summary>
public sealed partial class ConstructsDemo : Node3D
{
    private ConstructTestWorld? _world;
    private Camera3D _camera = null!;
    private string? _screenshot;
    private int _frames;
    private double _yaw;
    private double _pitch;

    /// <inheritdoc />
    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--screenshot=", StringComparison.Ordinal))
            {
                _screenshot = arg["--screenshot=".Length..];
            }
        }

        var origin = new Vector3(0, 4000, 0);
        AddChild(new DirectionalLight3D { Name = "Sun", RotationDegrees = new Vector3(-50, -35, 0), ShadowEnabled = true });
        AddChild(new WorldEnvironment
        {
            Name = "Environment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            },
        });
        AddChild(new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(600, 600) },
            Position = origin - new Vector3(0, 0.02, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.42f, 0.25f) },
        });

        var state = ConstructFixtures.Create(ConstructPattern.Building);
        _world = new ConstructTestWorld(this, state);
        _world.World.RebuildAll(ConstructTestWorld.Planet);

        _camera = new Camera3D { Name = "Camera", Far = 2000 };
        AddChild(_camera);
        _camera.Position = origin + new Vector3(70, 40, 120);
        _camera.LookAt(origin + new Vector3(32, 20, 32));
        _yaw = _camera.Rotation.Y;
        _pitch = _camera.Rotation.X;
    }

    /// <inheritdoc />
    public override void _ExitTree() => _world?.Dispose();

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        var speed = (Input.IsKeyPressed(Key.Shift) ? 60.0 : 15.0) * delta;
        var move = Vector3.Zero;
        move.X += (Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0);
        move.Z += (Input.IsKeyPressed(Key.S) ? 1 : 0) - (Input.IsKeyPressed(Key.W) ? 1 : 0);
        move.Y += (Input.IsKeyPressed(Key.E) ? 1 : 0) - (Input.IsKeyPressed(Key.Q) ? 1 : 0);
        _camera.Translate(move * speed);

        if (_screenshot is not null && ++_frames == 90 && _world!.World.PendingJobs == 0)
        {
            GetViewport().GetTexture().GetImage().SavePng(_screenshot);
            GetTree().Quit();
        }
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.IsMouseButtonPressed(MouseButton.Right))
        {
            _yaw -= motion.Relative.X * 0.003;
            _pitch = Math.Clamp(_pitch - (motion.Relative.Y * 0.003), -1.5, 1.5);
            _camera.Rotation = new Vector3(_pitch, _yaw, 0);
        }
    }
}
