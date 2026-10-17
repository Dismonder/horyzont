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
    /// <summary>Scene of the body: the capsule shape and an empty <c>Visual</c> node that a rendering client fills with the character model.</summary>
    public const string ScenePath = "res://scenes/Character/PlayerBody.tscn";

    /// <summary>Collision margin of the body (metres): a margin of a few millimetres keeps the floor contact steady at float32 precision 4 km from the origin.</summary>
    public const float SafeMarginMeters = 0.001f;

    /// <summary>Speed (m/s) with which a walking body is pressed into the floor so the engine keeps the floor contact and its snap.</summary>
    public const float FloorPressMetersPerSecond = 0.05f;

    private PlanetSpace _space;
    private PlanetParams? _planetParams;
    private uint _layer;
    private uint _mask;
    private MovementRules _movement = new();
    private bool _devFlightAllowed;
    private float _tickSeconds = 1f / 60f;
    private MovementMode _mode = MovementMode.Frozen;

    /// <summary>Gets the planet whose frame the body lives in.</summary>
    public PlanetId Planet => _space.Planet;

    /// <summary>Gets the space of the planet frame the body lives in (conversions through the planet node, K5-20).</summary>
    public PlanetSpace Space => _space;

    /// <summary>Gets a value indicating whether the body is attached to a construct frame: kinematic, without collision, posed by its owner.</summary>
    public bool Attached { get; private set; }

    /// <summary>Gets the movement mode after the last step.</summary>
    public MovementMode Mode => _mode;

    /// <summary>Gets a value indicating whether the body stood on the floor after the last step (or as placed).</summary>
    public bool OnFloor { get; private set; }

    /// <summary>Gets the feet position in the planet frame.</summary>
    public NumVector3 Feet => _space.ToPlanetLocal(GlobalPosition);

    /// <summary>Gets the velocity in the planet frame.</summary>
    public NumVector3 PlanetVelocity => _space.ToPlanetLocalDirection(Velocity);

    /// <summary>Gets the normalized body rotation in the planet frame.</summary>
    public NumQuaternion BodyRotation => _space.ToNumerics(GlobalBasis);

    /// <summary>Instantiates the scene body.</summary>
    /// <returns>A body that still needs <see cref="Configure(PlanetSpace, GameRules, MovementRules, bool, int, float)"/>.</returns>
    public static PlayerBody Instantiate() =>
        (GD.Load<PackedScene>(ScenePath) ?? throw new InvalidOperationException($"Scene {ScenePath} not found.")).Instantiate<PlayerBody>();

    /// <summary>Sets the rules, the planet and the collision layers with the planet standing in identity (tests, no frame nodes).</summary>
    /// <param name="planet">The planet (its parameters give the gravity).</param>
    /// <param name="rules">Capsule size.</param>
    /// <param name="movement">Movement rules.</param>
    /// <param name="devFlightAllowed">Whether the dev flight button is honoured.</param>
    /// <param name="layer">The layer number of the body (<see cref="CollisionLayers.ServerBodies"/> or <see cref="CollisionLayers.ClientBodies"/>).</param>
    /// <param name="tickSeconds">Length of a tick; the step the physics engine takes must equal it.</param>
    public void Configure(PlanetParams planet, GameRules rules, MovementRules movement, bool devFlightAllowed, int layer, float tickSeconds)
    {
        ArgumentNullException.ThrowIfNull(planet);
        Configure(new PlanetSpace(planet.Id, null, planet), rules, movement, devFlightAllowed, layer, tickSeconds);
    }

    /// <summary>
    /// Sets the frame space, the rules and the collision layers; call once before the body enters the tree. A walking body lives in a
    /// planet frame (K5-20, decision 18): its global values go through the planet node; a body in a construct frame is attached.
    /// </summary>
    /// <param name="space">The space of the planet frame, with the planet parameters (the gravity of the motor).</param>
    /// <param name="rules">Capsule size.</param>
    /// <param name="movement">Movement rules.</param>
    /// <param name="devFlightAllowed">Whether the dev flight button is honoured.</param>
    /// <param name="layer">The layer number of the body (<see cref="CollisionLayers.ServerBodies"/> or <see cref="CollisionLayers.ClientBodies"/>).</param>
    /// <param name="tickSeconds">Length of a tick; the step the physics engine takes must equal it.</param>
    public void Configure(PlanetSpace space, GameRules rules, MovementRules movement, bool devFlightAllowed, int layer, float tickSeconds)
    {
        var planet = space.Parameters ?? throw new ArgumentException("The space of a walking body needs the planet parameters.", nameof(space));
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(movement);
        _space = space;
        _planetParams = planet;
        _movement = movement;
        _devFlightAllowed = devFlightAllowed;
        _tickSeconds = tickSeconds;
        _layer = 1u << (layer - 1);
        _mask = CollisionLayers.TerrainMask | CollisionLayers.ConstructsMask;
        CollisionLayer = _layer;
        CollisionMask = _mask;

        // Kinematic movement on a sphere: up_direction is set every tick, so no global "up" exists for the engine.
        MotionMode = MotionModeEnum.Grounded;
        FloorMaxAngle = Mathf.DegToRad(movement.FloorMaxAngleDegrees);
        FloorSnapLength = movement.FloorSnapLength;
        FloorStopOnSlope = true;
        FloorBlockOnWall = false;
        MaxSlides = 6;
        SafeMargin = SafeMarginMeters;

        var capsule = GetNode<CollisionShape3D>("Shape");
        capsule.Shape = new CapsuleShape3D { Radius = rules.PlayerCapsuleRadius, Height = rules.PlayerCapsuleHeight };
        capsule.Position = new Vector3(0f, rules.PlayerCapsuleHeight * 0.5f, 0f);
    }

    /// <summary>Gets the node that holds the visual model (feet at its origin); empty on a server and on a client without rendering.</summary>
    public Node3D? Visual => GetNodeOrNull<Node3D>("Visual");

    /// <summary>Shows or hides the visual model.</summary>
    /// <param name="visible">Whether the model is shown.</param>
    public void SetVisualVisible(bool visible)
    {
        if (Visual is { } visual)
        {
            visual.Visible = visible;
        }
    }

    /// <summary>Applies a world-frame correction to the mesh only, leaving capsule, velocity and aim untouched.</summary>
    /// <param name="positionOffset">Planet-local visual displacement.</param>
    /// <param name="rotationOffset">Correction multiplied on the left of the body rotation.</param>
    public void ApplyPresentation(NumVector3 positionOffset, NumQuaternion rotationOffset)
    {
        if (Visual is not { } visual)
        {
            return;
        }

        var rotation = NumQuaternion.Normalize(rotationOffset * BodyRotation);
        var feet = Feet + positionOffset;
        visual.GlobalTransform = new Transform3D(_space.ToGodotBasis(rotation), _space.ToGodot(feet));
    }

    /// <summary>Puts the body at a state without moving through the world: spawn, teleport, a correction by the server.</summary>
    /// <param name="feet">Feet position in the planet frame.</param>
    /// <param name="velocity">Velocity in the planet frame.</param>
    /// <param name="rotation">Normalized body rotation.</param>
    /// <param name="mode">The mode to continue from.</param>
    /// <param name="onFloor">Whether the body stands on the floor.</param>
    /// <param name="resetInterpolation">Whether this is a visible discontinuity rather than a replay placement.</param>
    public void Place(NumVector3 feet, NumVector3 velocity, NumQuaternion rotation, MovementMode mode, bool onFloor, bool resetInterpolation = true)
    {
        GlobalPosition = _space.ToGodot(feet);
        GlobalBasis = _space.ToGodotBasis(rotation);
        Velocity = _space.ToGodotDirection(velocity);
        UpDirection = GlobalBasis.Y;
        _mode = mode;
        OnFloor = onFloor;
        if (resetInterpolation)
        {
            ResetPhysicsInterpolation();
        }
    }

    /// <summary>
    /// Attaches the body to a construct frame or lets it go (K5-20, decision 15): an attached body is frozen and has no collision (a body
    /// inside a hull must not collide with a flying ship); its owner poses it with <see cref="PlaceAttached"/>.
    /// </summary>
    /// <param name="attached">Whether the body is attached.</param>
    public void SetAttached(bool attached)
    {
        Attached = attached;
        CollisionLayer = attached ? 0u : _layer;
        CollisionMask = attached ? 0u : _mask;
        Velocity = Vector3.Zero;
        _mode = MovementMode.Frozen;
        OnFloor = false;
    }

    /// <summary>Poses an attached body in the Godot world (the frame pose composed with the local pose of the player).</summary>
    /// <param name="global">The global transform of the feet.</param>
    public void PlaceAttached(Transform3D global)
    {
        GlobalTransform = global;
        UpDirection = GlobalBasis.Y;
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
        GlobalBasis = _space.ToGodotBasis(output.BodyRotation);
        UpDirection = _space.ToGodotDirection(output.Up);
        var desired = output.DesiredVelocity;
        if (output.Mode is MovementMode.Walking or MovementMode.Sprinting)
        {
            // On the floor the motor asks for no radial velocity at all, and float rounding can leave a sliver of "up" in it; the engine
            // then skips the floor snap (it never snaps a body that moves up). A small press into the floor keeps the contact steady.
            desired += output.Up * (-FloorPressMetersPerSecond - NumVector3.Dot(desired, output.Up));
        }

        Velocity = _space.ToGodotDirection(desired);
        _mode = output.Mode;
        if (output.Mode != MovementMode.Frozen)
        {
            MotionMode = output.Mode == MovementMode.DevFlying ? MotionModeEnum.Floating : MotionModeEnum.Grounded;
            MoveAndSlide();

            // The state is what the network carries (float32): round the engine's doubles to it, so the server body and a client body
            // that starts from a snapshot hold bit-identical states and make the same contact decisions at the millimetre level.
            GlobalPosition = _space.ToGodot(Feet);
            Velocity = _space.ToGodotDirection(PlanetVelocity);
        }

        OnFloor = output.Mode != MovementMode.DevFlying && IsOnFloor();
        return _mode;
    }
}
