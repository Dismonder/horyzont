using System.Numerics;

namespace Horyzont.Client.Camera;

/// <summary>
/// The camera's "up" follows the body's up exponentially (time constant 0.12 s) with a rate limit of 180 degrees per second, so a
/// sudden change of the body (spawn, correction) does not roll the view. The eye position is never smoothed.
/// </summary>
public sealed class CameraUpSmoother
{
    private const float TimeConstantSeconds = 0.12f;
    private const float MaxRadiansPerSecond = MathF.PI;

    /// <summary>Initializes a new instance of the <see cref="CameraUpSmoother"/> class pointing along +Y.</summary>
    public CameraUpSmoother() => Up = Vector3.UnitY;

    /// <summary>Gets the smoothed unit up.</summary>
    public Vector3 Up { get; private set; }

    /// <summary>Sets the up at once: after a spawn, a correction with a reposition, and when leaving the developer camera.</summary>
    /// <param name="up">Unit up.</param>
    public void Snap(Vector3 up) => Up = up;

    /// <summary>Moves the smoothed up towards the target.</summary>
    /// <param name="targetUp">Unit up of the body.</param>
    /// <param name="dt">Tick length in seconds.</param>
    public void Advance(Vector3 targetUp, float dt)
    {
        var axis = Vector3.Cross(Up, targetUp);
        var sin = axis.Length();
        var angle = MathF.Atan2(sin, Vector3.Dot(Up, targetUp));
        if (angle < 1e-6f)
        {
            Up = targetUp;
            return;
        }

        var step = MathF.Min(angle * (1f - MathF.Exp(-dt / TimeConstantSeconds)), MaxRadiansPerSecond * dt);
        if (sin < 1e-6f)
        {
            // Exactly opposite: any axis perpendicular to the current up turns it.
            axis = Vector3.Cross(Up, MathF.Abs(Up.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY);
            sin = axis.Length();
        }

        var turn = Quaternion.CreateFromAxisAngle(axis / sin, step);
        Up = Vector3.Normalize(Vector3.Transform(Up, turn));
    }
}
