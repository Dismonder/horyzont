using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Core.Motion;

/// <summary>Input of one tick after dequantization and validation. Axes are exactly what the server decodes from the wire.</summary>
/// <param name="MoveX">Strafe axis, -1..1, a multiple of 1/127.</param>
/// <param name="MoveY">Forward axis, -1..1, a multiple of 1/127.</param>
/// <param name="Yaw">Yaw in (-pi, pi], see <see cref="SurfaceFrame"/>.</param>
/// <param name="Pitch">Pitch within the movement rules limit.</param>
/// <param name="Buttons">Held buttons.</param>
public readonly record struct MotorInput(float MoveX, float MoveY, float Yaw, float Pitch, InputButtons Buttons)
{
    /// <summary>
    /// Dequantizes the axes exactly as the codec does (<c>Quantization</c>), clamps each to -1..1, wraps yaw to (-pi, pi] and
    /// clamps pitch to the movement rules limit. A non-finite value gives <see cref="Neutral"/>(0, 0) and <c>false</c>; the caller
    /// substitutes its last yaw and pitch.
    /// </summary>
    /// <param name="input">Input from the wire or from the local sampler.</param>
    /// <param name="rules">Movement rules.</param>
    /// <param name="result">The motor input.</param>
    /// <returns>False when the input contained a non-finite value.</returns>
    public static bool TryFrom(in PlayerInput input, MovementRules rules, out MotorInput result) => throw new NotImplementedException();

    /// <summary>An input without movement and buttons that keeps the given view direction.</summary>
    /// <param name="yaw">Yaw.</param>
    /// <param name="pitch">Pitch.</param>
    /// <returns>The neutral input.</returns>
    public static MotorInput Neutral(float yaw, float pitch) => new(0f, 0f, yaw, pitch, InputButtons.None);
}
