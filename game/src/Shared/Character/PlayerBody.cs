using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Game.Frames;
using Horyzont.Game.Physics;

using NumQuaternion = System.Numerics.Quaternion;
using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Character;

/// <summary>
/// The body of a player: a <see cref="CharacterBody3D"/> with the capsule of <see cref="GameRules"/> whose origin is at the feet and
/// whose local +Y is the planet "up". The server owns one per player (layer <see cref="CollisionLayers.ServerBodies"/>), a client one
/// for its own predicted character (<see cref="CollisionLayers.ClientBodies"/>); both collide only with terrain and constructs, so
/// in a host the two bodies of one player never push each other (K2-11, decision 5). The movement itself is
/// <see cref="CharacterMotor"/> of Core; this node only applies its wanted velocity with <c>MoveAndSlide</c>.
/// </summary>
public sealed partial class PlayerBody : CharacterBody3D
{
    /// <summary>Scene of the placeholder capsule body.</summary>
    public const string ScenePath = "res://scenes/Character/PlayerBody.tscn";

    private PlanetId _planet;
    private PlanetParams? _planetParams;
    private MovementRules _movement = new();
    private bool _devFlightAllowed;
    private float _tickSeconds = 1f / 60f;
    private MovementMode _mode = MovementMode.Frozen;

    /// <summary>Gets the planet whose frame the body lives in.</summary>
    public PlanetId Planet => _planet;

    /// <summary>Gets the movement mode after the last step.</summary>
    public MovementMode Mode => _mode;

    /// <summary>Gets a value indicating whether the body stood on the floor after the last step (or as placed).</summary>
    public bool OnFloor { get; private set; }

    /// <summary>Gets the feet position in the planet frame.</summary>
    public NumVector3 Feet => PlanetFrames.ToPlanetLocal(_planet, GlobalPosition);

    /// <summary>Gets the velocity in the planet frame.</summary>
    public NumVector3 PlanetVelocity => PlanetFrames.ToPlanetLocalDirection(_planet, Velocity);

    /// <summary>Gets the normalized body rotation in the planet frame.</summary>
    public NumQuaternion BodyRotation => PlanetFrames.ToNumerics(_planet, GlobalBasis);

    /// <summary>Instantiates the scene body.</summary>
    /// <returns>A body that still needs <see cref="Configure"/>.</returns>
    public static PlayerBody Instantiate() =>
        (GD.Load<PackedScene>(ScenePath) ?? throw new InvalidOperationException($"Scene {ScenePath} not found.")).Instantiate<PlayerBody>();

    /// <summary>Sets the rules, the planet and the collision layers; call once before the body enters the tree.</summary>
    /// <param name="planet">The planet (its parameters give the gravity).</param>
    /// <param name="rules">Capsule size.</param>
    /// <param name="movement">Movement rules.</param>
    /// <param name="devFlightAllowed">Whether the dev flight button is honoured.</param>
    /// <param name="layer">The layer number of the body (<see cref="CollisionLayers.ServerBodies"/> or <see cref="CollisionLayers.ClientBodies"/>).</param>
    /// <param name="tickSeconds">Length of a tick; the step the physics engine takes must equal it.</param>
    public void Configure(PlanetParams planet, GameRules rules, MovementRules movement, bool devFlightAllowed, int layer, float tickSeconds)
    {
        ArgumentNullException.ThrowIfNull(planet);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(movement);
        _planet = planet.Id;
        _planetParams = planet;
        _movement = movement;
        _devFlightAllowed = devFlightAllowed;
        _tickSeconds = tickSeconds;
        CollisionLayer = 1u << (layer - 1);
        CollisionMask = CollisionLayers.TerrainMask | CollisionLayers.ConstructsMask;

        // Kinematic movement on a sphere: up_direction is set every tick, so no global "up" exists for the engine.
        MotionMode = MotionModeEnum.Grounded;
        FloorMaxAngle = Mathf.DegToRad(movement.FloorMaxAngleDegrees);
        FloorSnapLength = movement.FloorSnapLength;
        FloorStopOnSlope = true;
        FloorBlockOnWall = false;
        MaxSlides = 6;

        var capsule = GetNode<CollisionShape3D>("Shape");
        capsule.Shape = new CapsuleShape3D { Radius = rules.PlayerCapsuleRadius, Height = rules.PlayerCapsuleHeight };
        capsule.Position = new Vector3(0f, rules.PlayerCapsuleHeight * 0.5f, 0f);
        if (GetNodeOrNull<MeshInstance3D>("Visual") is { } visual)
        {
            visual.Mesh = new CapsuleMesh { Radius = rules.PlayerCapsuleRadius, Height = rules.PlayerCapsuleHeight };
            visual.Position = capsule.Position;
        }
    }

    /// <summary>Shows or hides the placeholder capsule mesh.</summary>
    /// <param name="visible">Whether the mesh is shown.</param>
    public void SetVisualVisible(bool visible)
    {
        if (GetNodeOrNull<MeshInstance3D>("Visual") is { } visual)
        {
            visual.Visible = visible;
        }
    }

    /// <summary>Puts the body at a state without moving through the world: spawn, teleport, a correction by the server.</summary>
    /// <param name="feet">Feet position in the planet frame.</param>
    /// <param name="velocity">Velocity in the planet frame.</param>
    /// <param name="rotation">Normalized body rotation.</param>
    /// <param name="mode">The mode to continue from.</param>
    /// <param name="onFloor">Whether the body stands on the floor.</param>
    public void Place(NumVector3 feet, NumVector3 velocity, NumQuaternion rotation, MovementMode mode, bool onFloor)
    {
        GlobalPosition = PlanetFrames.ToGodot(_planet, feet);
        GlobalBasis = PlanetFrames.ToGodotBasis(_planet, rotation);
        Velocity = PlanetFrames.ToGodotDirection(_planet, velocity);
        UpDirection = GlobalBasis.Y;
        _mode = mode;
        OnFloor = onFloor;
        ResetPhysicsInterpolation();
    }

    /// <summary>Sets the mode without moving the body (the server unfreezes a body this way).</summary>
    /// <param name="mode">The mode.</param>
    public void SetMode(MovementMode mode) => _mode = mode;

    /// <summary>
    /// One tick: <see cref="CharacterMotor"/> decides the wanted velocity, the engine moves the body. Must run inside a physics frame
    /// (<c>MoveAndSlide</c> uses the physics delta only there).
    /// </summary>
    /// <param name="input">Validated, quantized input.</param>
    /// <returns>The mode after the step.</returns>
    public MovementMode Step(in MotorInput input)
    {
        var planet = _planetParams ?? throw new InvalidOperationException("PlayerBody.Configure was not called.");
        var context = new MotorContext(Feet, PlanetVelocity, OnFloor, _mode, planet, _devFlightAllowed);
        var output = CharacterMotor.Step(input, context, _movement, _tickSeconds);
        GlobalBasis = PlanetFrames.ToGodotBasis(_planet, output.BodyRotation);
        UpDirection = PlanetFrames.ToGodotDirection(_planet, output.Up);
        Velocity = PlanetFrames.ToGodotDirection(_planet, output.DesiredVelocity);
        _mode = output.Mode;
        if (output.Mode != MovementMode.Frozen)
        {
            MotionMode = output.Mode == MovementMode.DevFlying ? MotionModeEnum.Floating : MotionModeEnum.Grounded;
            MoveAndSlide();
        }

        OnFloor = output.Mode != MovementMode.DevFlying && IsOnFloor();
        return _mode;
    }
}
