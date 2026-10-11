using Godot;

using Horyzont.Client;
using Horyzont.Game.Frames;
using Horyzont.Game.Physics;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Client.Character;

/// <summary>
/// The camera of the local player: the pose comes from <c>CameraRigMath</c> each tick (<see cref="LocalPlayer"/> calls
/// <see cref="Apply"/>), this node owns the <see cref="Camera3D"/>, the choice between third person (default) and first person
/// (<c>V</c>), and the collision of the third-person arm against terrain and constructs. The eye itself is never moved by the arm:
/// only the point behind the shoulder is pulled towards the eye.
/// </summary>
public sealed partial class CameraRig : Node3D
{
    /// <summary>Distance kept between the arm end and the surface it touches (metres).</summary>
    public const float ArmMargin = 0.2f;

    /// <summary>Furthest the camera sees (metres): the diameter of the planet plus room for the dev camera at orbit.</summary>
    public const float FarPlane = 40_000f;

    /// <summary>Creates the rig with its camera.</summary>
    public CameraRig()
    {
        Name = "CameraRig";
        Camera = new Camera3D { Name = "Camera", Near = 0.05f, Far = FarPlane, Fov = 75f };
        AddChild(Camera);
    }

    /// <summary>Raised when the player switches between first and third person.</summary>
    public event Action<CameraMode>? BodyModeChanged;

    /// <summary>Gets the camera.</summary>
    public Camera3D Camera { get; }

    /// <summary>Gets the camera mode of the body camera: <see cref="CameraMode.ThirdPerson"/> by default.</summary>
    public CameraMode BodyMode { get; private set; } = CameraMode.ThirdPerson;

    private PhysicsRayQueryParameters3D? _armQuery;

    /// <summary>Switches between first and third person.</summary>
    public void ToggleBodyMode()
    {
        BodyMode = BodyMode == CameraMode.ThirdPerson ? CameraMode.FirstPerson : CameraMode.ThirdPerson;
        BodyModeChanged?.Invoke(BodyMode);
    }

    /// <inheritdoc />
    public override void _Ready() => Camera.Current = true;

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("camera_toggle"))
        {
            ToggleBodyMode();
        }
    }

    /// <summary>Pulls the end of the third-person arm in front of the first surface between the eye and the wanted camera point.</summary>
    /// <param name="planet">The planet of the frame.</param>
    /// <param name="eye">The eye (start of the arm).</param>
    /// <param name="wanted">Where the camera would be without obstacles.</param>
    /// <returns>The camera point after the collision.</returns>
    public NumVector3 ResolveArm(PlanetSpace planet, NumVector3 eye, NumVector3 wanted)
    {
        var from = planet.ToGodot(eye);
        var to = planet.ToGodot(wanted);
        if (from.DistanceSquaredTo(to) < 1e-6)
        {
            return wanted;
        }

        // One query object per rig; the dictionary IntersectRay returns is allocated by the engine API and cannot be avoided.
        var query = _armQuery ??= new PhysicsRayQueryParameters3D { CollisionMask = CollisionLayers.TerrainMask | CollisionLayers.ConstructsMask };
        query.From = from;
        query.To = to;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            return wanted;
        }

        var point = hit["position"].AsVector3();
        var back = (from - point).Normalized();
        var distance = from.DistanceTo(point);
        var pulled = distance <= ArmMargin ? from : point + (back * ArmMargin);
        return planet.ToPlanetLocal(pulled);
    }

    /// <summary>Constrains a smoothed FPP or TPP camera from the physical aim eye.</summary>
    /// <param name="planet">Physical planet frame.</param>
    /// <param name="physicalEye">Unsmoothed eye used by tools.</param>
    /// <param name="visualOrigin">Wanted presentation position, including the correction.</param>
    /// <returns>A camera position on the eye side of the first wall.</returns>
    public NumVector3 ResolvePresentation(PlanetSpace planet, NumVector3 physicalEye, NumVector3 visualOrigin) =>
        ResolveArm(planet, physicalEye, visualOrigin);

    /// <summary>Places the camera.</summary>
    /// <param name="planet">The planet of the frame.</param>
    /// <param name="origin">Camera position in the planet frame.</param>
    /// <param name="direction">Unit viewing direction.</param>
    /// <param name="up">Unit up of the camera (the roll follows it).</param>
    /// <param name="teleport">Whether the camera jumps instead of moving (no physics interpolation across the jump).</param>
    public void Apply(PlanetSpace planet, NumVector3 origin, NumVector3 direction, NumVector3 up, bool teleport)
    {
        var look = planet.ToGodotDirection(direction);
        var roll = planet.ToGodotDirection(up);
        if (Mathf.Abs(look.Normalized().Dot(roll.Normalized())) > 0.9999)
        {
            // Looking straight along the up (a pole of the pitch range): any perpendicular reference keeps the basis valid.
            roll = Mathf.Abs(look.Normalized().Dot(Vector3.Right)) < 0.9 ? Vector3.Right : Vector3.Forward;
        }

        Camera.GlobalTransform = new Transform3D(Basis.LookingAt(look, roll), planet.ToGodot(origin));
        if (teleport)
        {
            Camera.ResetPhysicsInterpolation();
        }
    }
}
