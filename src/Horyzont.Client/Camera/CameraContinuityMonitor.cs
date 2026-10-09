using System.Numerics;

namespace Horyzont.Client.Camera;

/// <summary>
/// Detects camera jumps for tests, the developer HUD and the log: a rotation larger than the allowed angle that the mouse does not
/// explain, or a displacement larger than the speed allows. A declared <see cref="Snap"/> is not a jump.
/// </summary>
public sealed class CameraContinuityMonitor
{
    private const float DistanceSlackMeters = 0.01f;

    private readonly float _maxSpeed;
    private readonly float _dt;
    private readonly float _maxAngle;
    private bool _hasBaseline;
    private Vector3 _position;
    private Vector3 _direction;

    /// <summary>Initializes a new instance of the <see cref="CameraContinuityMonitor"/> class.</summary>
    /// <param name="maxSpeedMetersPerSecond">Largest speed the camera may legitimately have.</param>
    /// <param name="tickSeconds">Tick length in seconds.</param>
    /// <param name="maxAngleRadiansPerTick">Largest rotation per tick that is not a jump (2 degrees by default).</param>
    public CameraContinuityMonitor(float maxSpeedMetersPerSecond, float tickSeconds, float maxAngleRadiansPerTick = 2f * MathF.PI / 180f)
    {
        _maxSpeed = maxSpeedMetersPerSecond;
        _dt = tickSeconds;
        _maxAngle = maxAngleRadiansPerTick;
    }

    /// <summary>Gets the number of jumps reported so far.</summary>
    public int JumpCount { get; private set; }

    /// <summary>Gets the rotation of the latest reported jump in radians.</summary>
    public float LastJumpAngle { get; private set; }

    /// <summary>Gets the displacement of the latest reported jump in metres.</summary>
    public float LastJumpDistance { get; private set; }

    /// <summary>Declares an intended discontinuity (spawn, correction with a reposition, leaving the developer camera).</summary>
    public void Snap() => _hasBaseline = false;

    /// <summary>Compares the camera of this tick with the previous one.</summary>
    /// <param name="position">Camera origin.</param>
    /// <param name="direction">Unit viewing direction.</param>
    /// <param name="mouseTurnRadians">How far the mouse turned the view this tick.</param>
    /// <returns>True when the change is a jump.</returns>
    public bool Observe(Vector3 position, Vector3 direction, float mouseTurnRadians)
    {
        var jumped = false;
        if (_hasBaseline)
        {
            var angle = MathF.Atan2(Vector3.Cross(_direction, direction).Length(), Vector3.Dot(_direction, direction));
            var distance = (position - _position).Length();
            if (angle > _maxAngle + MathF.Abs(mouseTurnRadians) || distance > (_maxSpeed * _dt) + DistanceSlackMeters)
            {
                jumped = true;
                JumpCount++;
                LastJumpAngle = angle;
                LastJumpDistance = distance;
            }
        }

        _position = position;
        _direction = direction;
        _hasBaseline = true;
        return jumped;
    }
}
