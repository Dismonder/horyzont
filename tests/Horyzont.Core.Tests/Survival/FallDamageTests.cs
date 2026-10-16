using System.Numerics;

using Horyzont.Core.Motion;
using Horyzont.Core.Survival;

namespace Horyzont.Core.Tests.Survival;

/// <summary>K6-33, D18: the damage of a fall by the impact speed. <see cref="Compute_15MetresPerSecond_32Points"/> is the acceptance criterion of the spec (l. 123).</summary>
public sealed class FallDamageTests
{
    private static readonly VitalsRules _rules = VitalsRules.Default;
    private static readonly Vector3 _up = Vector3.UnitY;

    [Fact]
    public void Compute_AtOrBelowSafeSpeed_Zero()
    {
        // The accepted counterpart: above the safe speed there is damage.
        Assert.True(FallDamage.Compute(11.5f, _rules) > 0f);

        Assert.Equal(0f, FallDamage.Compute(0f, _rules));
        Assert.Equal(0f, FallDamage.Compute(5f, _rules));
        Assert.Equal(0f, FallDamage.Compute(11f, _rules));
        Assert.Equal(0f, FallDamage.Compute(-3f, _rules));
    }

    [Fact]
    public void Compute_15MetresPerSecond_32Points()
    {
        // 8 points per m/s above 11 m/s: (15 - 11) * 8 = 32.
        Assert.Equal(32.0, FallDamage.Compute(15f, _rules), 4);
        Assert.Equal(4.0, FallDamage.Compute(11.5f, _rules), 4);
        Assert.Equal(8.0, FallDamage.Compute(12f, _rules), 4);
    }

    [Fact]
    public void Compute_UsesTheRules()
    {
        var gentle = _rules with { FallSafeSpeed = 20f, FallDamagePerMetrePerSecond = 1f };

        Assert.Equal(0f, FallDamage.Compute(15f, gentle));
        Assert.Equal(5.0, FallDamage.Compute(25f, gentle), 4);
    }

    [Fact]
    public void TryDetect_FallingToWalking_UsesPreviousVelocity()
    {
        // The velocity of the tick before the landing; the engine zeroes the radial part on landing, so the current velocity would say nothing.
        Assert.True(FallImpact.TryDetect(MovementMode.Falling, new Vector3(0f, -15f, 0f), _up, MovementMode.Walking, out var speed));
        Assert.Equal(15f, speed, 4);

        // Only the part along "up" counts: sideways speed does not, and another "up" (a point on the planet) works the same.
        Assert.True(FallImpact.TryDetect(MovementMode.Falling, new Vector3(3f, -12f, 2f), _up, MovementMode.Walking, out var sideways));
        Assert.Equal(12f, sideways, 4);
        Assert.True(FallImpact.TryDetect(MovementMode.Falling, new Vector3(-9f, 1f, 0f), Vector3.UnitX, MovementMode.Walking, out var other));
        Assert.Equal(9f, other, 4);
    }

    [Fact]
    public void TryDetect_LandingWhileMovingUp_ZeroImpact()
    {
        // A landing with a velocity that points up (a step, a lift) is a landing without an impact: never a negative speed.
        Assert.True(FallImpact.TryDetect(MovementMode.Falling, new Vector3(0f, 2f, 0f), _up, MovementMode.Walking, out var speed));
        Assert.Equal(0f, speed);
    }

    [Fact]
    public void TryDetect_JumpingToSprinting_Detected()
    {
        Assert.True(FallImpact.TryDetect(MovementMode.Jumping, new Vector3(0f, -12f, 0f), _up, MovementMode.Sprinting, out var speed));
        Assert.Equal(12f, speed, 4);
    }

    [Fact]
    public void TryDetect_StillFalling_None()
    {
        // The accepted counterpart first.
        Assert.True(FallImpact.TryDetect(MovementMode.Falling, new Vector3(0f, -20f, 0f), _up, MovementMode.Walking, out _));

        Assert.False(FallImpact.TryDetect(MovementMode.Falling, new Vector3(0f, -20f, 0f), _up, MovementMode.Falling, out var a));
        Assert.False(FallImpact.TryDetect(MovementMode.Falling, new Vector3(0f, -20f, 0f), _up, MovementMode.Jumping, out var b));
        Assert.False(FallImpact.TryDetect(MovementMode.Jumping, new Vector3(0f, -20f, 0f), _up, MovementMode.Falling, out var c));
        Assert.False(FallImpact.TryDetect(MovementMode.Walking, new Vector3(0f, -20f, 0f), _up, MovementMode.Walking, out var d));
        Assert.False(FallImpact.TryDetect(MovementMode.Walking, new Vector3(0f, -20f, 0f), _up, MovementMode.Falling, out var e));
        Assert.Equal(0f, a + b + c + d + e);
    }

    [Fact]
    public void TryDetect_FromFrozenOrDevFlying_None()
    {
        Assert.True(FallImpact.TryDetect(MovementMode.Falling, new Vector3(0f, -20f, 0f), _up, MovementMode.Walking, out _));

        // A frozen body (spawn, rescue) or a flying developer does not take fall damage, whatever its velocity says, and a fall that ends frozen or flying neither.
        Assert.False(FallImpact.TryDetect(MovementMode.Frozen, new Vector3(0f, -20f, 0f), _up, MovementMode.Walking, out _));
        Assert.False(FallImpact.TryDetect(MovementMode.DevFlying, new Vector3(0f, -20f, 0f), _up, MovementMode.Walking, out _));
        Assert.False(FallImpact.TryDetect(MovementMode.Falling, new Vector3(0f, -20f, 0f), _up, MovementMode.Frozen, out _));
        Assert.False(FallImpact.TryDetect(MovementMode.Falling, new Vector3(0f, -20f, 0f), _up, MovementMode.DevFlying, out _));
    }
}
