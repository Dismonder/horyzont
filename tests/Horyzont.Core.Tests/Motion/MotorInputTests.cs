using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;

namespace Horyzont.Core.Tests.Motion;

/// <summary>K2-11 step 2: the validated input of one tick.</summary>
public sealed class MotorInputTests
{
    private static readonly MovementRules _rules = new();

    private static PlayerInput Raw(float moveX = 0f, float moveY = 0f, float yaw = 0f, float pitch = 0f, InputButtons buttons = InputButtons.None) =>
        new(1, moveX, moveY, yaw, pitch, buttons);

    [Theory]
    [InlineData(0.5f)]
    [InlineData(-0.3f)]
    [InlineData(0.004f)]
    [InlineData(1f)]
    [InlineData(-1f)]
    [InlineData(0f)]
    public void MotorInput_Axes_AreDequantizedExactlyLikeTheCodec(float axis)
    {
        var expected = Quantization.DequantizeAxis(Quantization.QuantizeAxis(axis));

        var ok = MotorInput.TryFrom(Raw(axis, -axis), _rules, out var result);

        Assert.True(ok);
        Assert.Equal(expected, result.MoveX);
        Assert.Equal(-expected, result.MoveY);
    }

    [Fact]
    public void MotorInput_AxesFromTheWire_AreUnchanged()
    {
        // A value the server decoded from the wire is already a multiple of 1/127 and must survive a second quantization.
        for (var q = -127; q <= 127; q++)
        {
            var wire = Quantization.DequantizeAxis((sbyte)q);

            Assert.True(MotorInput.TryFrom(Raw(wire, wire), _rules, out var result));
            Assert.Equal(wire, result.MoveX);
            Assert.Equal(wire, result.MoveY);
        }
    }

    [Fact]
    public void MotorInput_OutOfRangeAxes_ClampedToOne()
    {
        Assert.True(MotorInput.TryFrom(Raw(5f, -7f), _rules, out var result));

        Assert.Equal(1f, result.MoveX);
        Assert.Equal(-1f, result.MoveY);
    }

    [Fact]
    public void MotorInput_Pitch_ClampedToMaxPitch()
    {
        Assert.True(MotorInput.TryFrom(Raw(pitch: 3f), _rules, out var up));
        Assert.True(MotorInput.TryFrom(Raw(pitch: -3f), _rules, out var down));
        Assert.True(MotorInput.TryFrom(Raw(pitch: 0.5f), _rules, out var inside));

        Assert.Equal(_rules.MaxPitch, up.Pitch);
        Assert.Equal(-_rules.MaxPitch, down.Pitch);
        Assert.Equal(0.5f, inside.Pitch);
        Assert.InRange(_rules.MaxPitch, 89f * MathF.PI / 180f - 1e-3f, 89f * MathF.PI / 180f + 1e-3f);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 1f)]
    [InlineData(MathF.PI, MathF.PI)]
    [InlineData(-MathF.PI, MathF.PI)]
    [InlineData(7f, 7f - (2f * MathF.PI))]
    [InlineData(-7f, -7f + (2f * MathF.PI))]
    public void MotorInput_Yaw_WrappedToHalfOpenRange(float yaw, float expected)
    {
        Assert.True(MotorInput.TryFrom(Raw(yaw: yaw), _rules, out var result));

        Assert.Equal(expected, result.Yaw, 1e-4f);
    }

    [Theory]
    [InlineData(1e6f)]
    [InlineData(-1e6f)]
    [InlineData(float.MaxValue)]
    [InlineData(float.MinValue)]
    public void MotorInput_Yaw_HugeValueStaysInRange(float yaw)
    {
        Assert.True(MotorInput.TryFrom(Raw(yaw: yaw), _rules, out var result));

        Assert.True(result.Yaw > -MathF.PI && result.Yaw <= MathF.PI, $"yaw {result.Yaw}");
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0f, 0f)]
    [InlineData(float.PositiveInfinity, 0f, 0f, 0f)]
    [InlineData(0f, float.NegativeInfinity, 0f, 0f)]
    [InlineData(0f, 0f, float.NaN, 0f)]
    [InlineData(0f, 0f, float.PositiveInfinity, 0f)]
    [InlineData(0f, 0f, 0f, float.NaN)]
    [InlineData(0f, 0f, 0f, float.NegativeInfinity)]
    public void MotorInput_NonFinite_ReturnsFalseAndNeutral(float moveX, float moveY, float yaw, float pitch)
    {
        var ok = MotorInput.TryFrom(Raw(moveX, moveY, yaw, pitch, InputButtons.Jump | InputButtons.Sprint), _rules, out var result);

        Assert.False(ok);
        Assert.Equal(MotorInput.Neutral(0f, 0f), result);
    }

    [Fact]
    public void MotorInput_Buttons_PassThrough()
    {
        var buttons = InputButtons.Jump | InputButtons.Sprint | InputButtons.DevFly;

        Assert.True(MotorInput.TryFrom(Raw(buttons: buttons), _rules, out var result));

        Assert.Equal(buttons, result.Buttons);
    }

    [Fact]
    public void MotorInput_Neutral_KeepsViewAndDropsMovementAndButtons()
    {
        var neutral = MotorInput.Neutral(1.5f, -0.25f);

        Assert.Equal(new MotorInput(0f, 0f, 1.5f, -0.25f, InputButtons.None), neutral);
    }
}
