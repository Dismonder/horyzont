using Godot;

using Horyzont.Client.Camera;
using Horyzont.Client.Dev;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Terrain;

using NumMatrix = System.Numerics.Matrix4x4;
using NumQuaternion = System.Numerics.Quaternion;
using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Client.Character;

/// <summary>
/// The free camera of development builds (spec: "Kamera deweloperska"): <c>F8</c> detaches the camera from the body (the body stands
/// and sends neutral input), <c>F9</c> moves it to orbit (twice the radius from the centre, looking at it). It flies without
/// collisions at <see cref="DevFlightSpeed"/> (the mouse wheel scales it), never further than the dev flight limit from the centre.
/// This node holds the state and the integration; <see cref="LocalPlayer"/> decides when the camera starts and stops and moves the
/// terrain viewer with it.
/// </summary>
public sealed partial class DevFlyCamera : Node
{
    /// <summary>Speed factor of a held sprint key.</summary>
    public const float SprintFactor = 4f;

    /// <summary>Distance of the orbit position from the centre, in planet radii (owner question P8).</summary>
    public const float OrbitRadiusFactor = 2f;

    private const float WheelStep = 1.25f;
    private const float MinMultiplier = 0.05f;
    private const float MaxMultiplier = 50f;

    private readonly OrientationTracker _orientation;
    private readonly CameraUpSmoother _smoother = new();
    private readonly MovementRules _movement;
    private float _multiplier = 1f;

    /// <summary>Creates the camera.</summary>
    /// <param name="movement">Movement rules (the pitch limit and the flight limit).</param>
    public DevFlyCamera(MovementRules movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        _movement = movement;
        _orientation = new OrientationTracker(movement);
        Name = "DevFlyCamera";
    }

    /// <summary>Raised when <c>F8</c> is pressed.</summary>
    public event Action? ToggleRequested;

    /// <summary>Raised when <c>F9</c> is pressed.</summary>
    public event Action? OrbitRequested;

    /// <summary>Gets a value indicating whether the camera is detached from the body.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets the radius of the planet in metres (set from the server hello; 4000 m before).</summary>
    public float PlanetRadius { get; private set; } = 4000f;

    /// <summary>Gets the camera position in the planet frame.</summary>
    public NumVector3 Position { get; private set; }

    /// <summary>Gets the unit viewing direction.</summary>
    public NumVector3 Direction { get; private set; } = -NumVector3.UnitZ;

    /// <summary>Gets the smoothed up of the camera (the roll follows it).</summary>
    public NumVector3 Up => _smoother.Up;

    /// <summary>Gets the speed multiplier set with the mouse wheel.</summary>
    public float SpeedMultiplier => _multiplier;

    /// <summary>Gets the speed at the current height, in metres per second.</summary>
    public float Speed => DevFlightSpeed.At(Position.Length() - PlanetRadius) * _multiplier;

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("dev_camera_toggle"))
        {
            ToggleRequested?.Invoke();
        }
        else if (@event.IsActionPressed("dev_camera_orbit"))
        {
            OrbitRequested?.Invoke();
        }
    }

    /// <summary>Tells the camera the radius of the planet it flies around.</summary>
    /// <param name="radius">Radius in metres.</param>
    public void SetPlanetRadius(float radius) => PlanetRadius = radius;

    /// <summary>Detaches the camera at a pose.</summary>
    /// <param name="position">Position in the planet frame.</param>
    /// <param name="direction">Viewing direction.</param>
    public void Enter(NumVector3 position, NumVector3 direction)
    {
        IsActive = true;
        Place(position, direction);
    }

    /// <summary>Returns the camera to the body.</summary>
    public void Exit() => IsActive = false;

    /// <summary>Moves the camera above a direction at orbit height, looking at the centre of the planet.</summary>
    /// <param name="above">Direction from the centre (the up of the body); a degenerate vector falls back to +Y.</param>
    public void GoToOrbit(NumVector3 above)
    {
        var up = above.LengthSquared() > 1e-6f ? NumVector3.Normalize(above) : NumVector3.UnitY;
        IsActive = true;
        Place(up * (OrbitRadiusFactor * PlanetRadius), -up);
    }

    /// <summary>Flies for one tick.</summary>
    /// <param name="input">The input of the tick (the mouse turns the view, the keys move; the sprint key speeds up).</param>
    /// <param name="dt">Tick length in seconds.</param>
    public void Advance(in LocalInputSample input, float dt)
    {
        _multiplier = Math.Clamp(_multiplier * MathF.Pow(WheelStep, input.WheelSteps), MinMultiplier, MaxMultiplier);
        var up = TerrainMath.UpAt(Position);
        _orientation.Advance(up, input.MouseYaw, input.MousePitch);
        var heading = Vector3Of(_orientation.BodyRotation, -NumVector3.UnitZ);
        var right = Vector3Of(_orientation.BodyRotation, NumVector3.UnitX);
        var look = (heading * MathF.Cos(_orientation.Pitch)) + (up * MathF.Sin(_orientation.Pitch));
        var vertical = ((input.Buttons & InputButtons.Jump) != 0 ? 1f : 0f) - ((input.Buttons & InputButtons.Crouch) != 0 ? 1f : 0f);
        var wish = (look * input.MoveY) + (right * input.MoveX) + (up * vertical);
        var length = wish.Length();
        if (length > 1f)
        {
            wish /= length;
        }

        var speed = Speed * ((input.Buttons & InputButtons.Sprint) != 0 ? SprintFactor : 1f);
        var next = Position + (wish * (speed * dt));
        var distance = next.Length();
        var maxRadius = _movement.MaxDevFlightRadiusFactor * PlanetRadius;
        if (distance > maxRadius)
        {
            next *= maxRadius / distance;
        }

        Position = next;
        Direction = NumVector3.Normalize(look);
        _smoother.Advance(TerrainMath.UpAt(Position), dt);
    }

    private static NumVector3 Vector3Of(NumQuaternion rotation, NumVector3 axis) => NumVector3.Transform(axis, rotation);

    private void Place(NumVector3 position, NumVector3 direction)
    {
        Position = position;
        var look = direction.LengthSquared() > 1e-6f ? NumVector3.Normalize(direction) : -NumVector3.UnitZ;
        var up = TerrainMath.UpAt(position);
        var pitch = MathF.Asin(Math.Clamp(NumVector3.Dot(look, up), -1f, 1f));
        var heading = look - (up * NumVector3.Dot(look, up));
        heading = heading.LengthSquared() > 1e-8f ? NumVector3.Normalize(heading) : NumVector3.Normalize(SurfaceFrame.Reference(up));

        // Rotation whose local -Z is the heading and whose local +Y is up (columns: right, up, back).
        var back = -heading;
        var right = NumVector3.Cross(up, back);
        var basis = new NumMatrix(
            right.X, right.Y, right.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            back.X, back.Y, back.Z, 0f,
            0f, 0f, 0f, 1f);
        _orientation.Reset(NumQuaternion.CreateFromRotationMatrix(basis), pitch);
        Direction = look;
        _smoother.Snap(up);
    }
}
