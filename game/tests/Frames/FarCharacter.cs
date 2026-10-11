using Godot;

using Horyzont.Core.Motion;
using Horyzont.Core.Simulation;
using Horyzont.Game.Character;
using Horyzont.Game.Physics;

namespace Horyzont.Game.Tests.Frames;

/// <summary>
/// A character body with the physics configuration of <see cref="PlayerBody"/> (capsule of <see cref="GameRules"/>, safe margin,
/// floor snap and the press into the floor), moved in double precision with gravity towards a test centre. <see cref="PlayerBody"/>
/// itself lives in the planet frame through <c>PlanetFrames</c> (planet at the origin, 100 km range, float32 state), so it cannot
/// stand 115 km from the origin before K5-20 phase D; this body measures the engine (Jolt in double precision) alone.
/// </summary>
public sealed partial class FarCharacter : CharacterBody3D
{
    private readonly MovementRules _movement = new();
    private Vector3 _centre;
    private double _gravity = 9.81;
    private bool _flatDown;

    /// <summary>Gets a value indicating whether the body stood on the floor after the last step.</summary>
    public bool OnFloor { get; private set; }

    /// <summary>Gets the "up" direction used in the last step.</summary>
    public Vector3 Up => _flatDown ? Vector3.Up : (GlobalPosition - _centre).Normalized();

    /// <summary>Creates a body with the capsule of <paramref name="rules"/>; call before the body enters the tree.</summary>
    /// <param name="rules">Capsule size.</param>
    /// <param name="centre">Gravity centre in Godot space (ignored when <paramref name="flatDown"/>).</param>
    /// <param name="flatDown">True for gravity along -Y.</param>
    /// <param name="gravity">Gravity in m/s².</param>
    /// <returns>The body.</returns>
    public static FarCharacter Create(GameRules rules, Vector3 centre, bool flatDown = false, double gravity = 9.81)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var body = new FarCharacter { Name = "FarCharacter", _centre = centre, _flatDown = flatDown, _gravity = gravity };
        body.CollisionLayer = CollisionLayers.ServerBodiesMask;
        body.CollisionMask = CollisionLayers.TerrainMask | CollisionLayers.ConstructsMask;
        body.MotionMode = MotionModeEnum.Grounded;
        body.FloorMaxAngle = Mathf.DegToRad(body._movement.FloorMaxAngleDegrees);
        body.FloorSnapLength = body._movement.FloorSnapLength;
        body.FloorStopOnSlope = true;
        body.FloorBlockOnWall = false;
        body.MaxSlides = 6;
        body.SafeMargin = PlayerBody.SafeMarginMeters;
        var shape = new CollisionShape3D
        {
            Name = "Shape",
            Shape = new CapsuleShape3D { Radius = rules.PlayerCapsuleRadius, Height = rules.PlayerCapsuleHeight },
            Position = new Vector3(0, rules.PlayerCapsuleHeight * 0.5, 0),
        };
        body.AddChild(shape);
        return body;
    }

    /// <summary>Puts the body at feet position <paramref name="feet"/>, upright, at rest.</summary>
    /// <param name="feet">Feet position in Godot space.</param>
    public void Place(Vector3 feet)
    {
        GlobalPosition = feet;
        AlignToUp();
        Velocity = Vector3.Zero;
        OnFloor = false;
    }

    /// <summary>One physics tick: gravity in the air, the <see cref="PlayerBody"/> press into the floor on the ground.</summary>
    /// <param name="delta">Tick length in seconds.</param>
    /// <param name="jump">True to take off with <see cref="MovementRules.JumpSpeed"/>.</param>
    public void Step(double delta, bool jump = false)
    {
        AlignToUp();
        var up = Up;
        UpDirection = up;
        if (OnFloor && jump)
        {
            Velocity = up * _movement.JumpSpeed;
        }
        else if (OnFloor)
        {
            Velocity = up * -PlayerBody.FloorPressMetersPerSecond;
        }
        else
        {
            Velocity += up * (-_gravity * delta);
        }

        MoveAndSlide();
        OnFloor = IsOnFloor();
    }

    private void AlignToUp()
    {
        var up = Up;
        var reference = Mathf.Abs(up.Dot(Vector3.Forward)) < 0.9 ? Vector3.Forward : Vector3.Right;
        var right = reference.Cross(up).Normalized();
        var forward = up.Cross(right).Normalized();
        GlobalBasis = new Basis(right, up, -forward).Orthonormalized();
    }
}
