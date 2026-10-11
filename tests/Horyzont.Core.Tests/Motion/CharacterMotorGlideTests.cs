using System.Numerics;

using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Motion;

/// <summary>K5-25 decision 19: the suit glide after an ejection.</summary>
public sealed class CharacterMotorGlideTests
{
    private const float Dt = 1f / 60f;
    private static readonly MovementRules _rules = new();
    private static readonly Vector3 _feet = new(0f, 4100f, 0f);

    private static MotorOutput Step(Vector3 velocity, bool onFloor = false, MovementMode mode = MovementMode.Gliding, float moveY = 0f) =>
        CharacterMotor.Step(
            new MotorInput(0f, moveY, 0f, 0f, InputButtons.None),
            new MotorContext(_feet, velocity, onFloor, mode, WorldFactory.HomePlanetParams, false),
            _rules,
            Dt);

    [Fact]
    public void CharacterMotor_Gliding_FallSpeedClampedTo6()
    {
        var velocity = new Vector3(0f, -25f, 0f);
        for (var i = 0; i < 300; i++)
        {
            var output = Step(velocity);
            Assert.Equal(MovementMode.Gliding, output.Mode);
            velocity = output.DesiredVelocity;
            Assert.True(velocity.Y >= -6f - 1e-4f, velocity.ToString());
        }

        Assert.Equal(-6f, velocity.Y, 3);
    }

    [Fact]
    public void CharacterMotor_Gliding_InheritsVelocityThenDamps()
    {
        var velocity = new Vector3(20f, 0f, 0f);

        var first = Step(velocity).DesiredVelocity;
        Assert.Equal(20f * (1f - (0.5f * Dt)), first.X, 4);
        for (var i = 0; i < 120; i++)
        {
            velocity = Step(velocity).DesiredVelocity;
        }

        Assert.InRange(velocity.X, 20f * MathF.Exp(-1f) * 0.98f, 20f * MathF.Exp(-1f) * 1.02f);
    }

    [Fact]
    public void CharacterMotor_Gliding_FloorContact_Walking()
    {
        var output = Step(new Vector3(3f, -6f, 0f), onFloor: true);

        Assert.Equal(MovementMode.Walking, output.Mode);
        Assert.Equal(MovementMode.Falling, Step(new Vector3(0f, -25f, 0f), mode: MovementMode.Falling).Mode);
        Assert.True(Step(new Vector3(0f, -59f, 0f), mode: MovementMode.Falling).DesiredVelocity.Y < -58f);
    }
}
