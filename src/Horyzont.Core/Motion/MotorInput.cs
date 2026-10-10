using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;

namespace Horyzont.Core.Motion;

/// <summary>Input of one tick after dequantization and validation. Axes are exactly what the server decodes from the wire.</summary>
/// <param name="MoveX">Strafe axis, -1..1, a multiple of 1/127.</param>
/// <param name="MoveY">Forward axis, -1..1, a multiple of 1/127.</param>
/// <param name="Yaw">Yaw in (-pi, pi], see <see cref="SurfaceFrame"/>.</param>
/// <param name="Pitch">Pitch within the movement rules limit.</param>
/// <param name="Buttons">Held buttons.</param>
public readonly record struct MotorInput(float MoveX, float MoveY, float Yaw, float Pitch, InputButtons Buttons)
{
    private const float TwoPi = 2f * MathF.PI;

    /// <summary>
    /// Dequantizes the axes exactly as the codec does (<c>Quantization</c>), clamps each to -1..1, wraps yaw to (-pi, pi] and
    /// clamps pitch to the movement rules limit. A non-finite value gives <see cref="Neutral"/>(0, 0) and <c>false</c>; the caller
    /// substitutes its last yaw and pitch.
    /// </summary>
    /// <param name="input">Input from the wire or from the local sampler.</param>
    /// <param name="rules">Movement rules.</param>
    /// <param name="result">The motor input.</param>
    /// <returns>False when the input contained a non-finite value.</returns>
    public static bool TryFrom(in PlayerInput input, MovementRules rules, out MotorInput result)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (!float.IsFinite(input.MoveX) || !float.IsFinite(input.MoveY) || !float.IsFinite(input.Yaw) || !float.IsFinite(input.Pitch))
        {
            result = Neutral(0f, 0f);
            return false;
        }

        var yaw = MathF.IEEERemainder(input.Yaw, TwoPi);
        if (yaw <= -MathF.PI)
        {
            yaw += TwoPi;
        }

        result = new MotorInput(
            Quantization.DequantizeAxis(Quantization.QuantizeAxis(input.MoveX)),
            Quantization.DequantizeAxis(Quantization.QuantizeAxis(input.MoveY)),
            yaw,
            Math.Clamp(input.Pitch, -rules.MaxPitch, rules.MaxPitch),
            input.Buttons);
        return true;
    }

    /// <summary>An input without movement and buttons that keeps the given view direction.</summary>
    /// <param name="yaw">Yaw.</param>
    /// <param name="pitch">Pitch.</param>
    /// <returns>The neutral input.</returns>
    public static MotorInput Neutral(float yaw, float pitch) => new(0f, 0f, yaw, pitch, InputButtons.None);
}
