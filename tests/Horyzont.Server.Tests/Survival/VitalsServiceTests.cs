using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Motion;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.World;
using Horyzont.Server.Survival;
using Horyzont.Testing;

namespace Horyzont.Server.Tests.Survival;

/// <summary>
/// <see cref="VitalsService"/> (K6-33 D13, D15, D16, D18, D22): health and stamina of the players online every tick, the costs of sprinting, jumping and tools,
/// the damage of falls, the food, and message 44 about the player to the player. The defaults are 100 health and 100 stamina, 8 stamina per second of sprinting,
/// 10 per jump, 4 per use of a tool, 11 m/s safe speed and 8 health per metre per second above it, at 60 ticks per second.
/// </summary>
public sealed class VitalsServiceTests
{
    private static readonly Vector3 _fall15 = new(0f, -15f, 0f);

    private readonly FoodServerRig _rig = new();

    private PlayerVitals Vitals(PlayerId player) => _rig.VitalsOf(player) ?? throw new InvalidOperationException("no vitals");

    private void Walk(int ticks)
    {
        _rig.Motion.Set(_rig.Alice, MovementMode.Walking);
        _rig.Tick(ticks);
    }

    [Fact]
    public void Tick_FreshPlayer_FullMaxima()
    {
        Assert.Null(_rig.VitalsOf(_rig.Alice));

        Walk(1);

        Assert.Equal(100f, Vitals(_rig.Alice).Health, 0.01f);
        Assert.Equal(100f, Vitals(_rig.Alice).Stamina, 0.01f);
        Assert.False(Vitals(_rig.Alice).Exhausted);

        // A player without a body is not simulated (Bob has no motion).
        Assert.Null(_rig.VitalsOf(_rig.Bob));
    }

    [Fact]
    public void Tick_Sprinting_DrainsStamina()
    {
        Walk(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Sprinting);

        _rig.Tick(60);

        Assert.Equal(92f, Vitals(_rig.Alice).Stamina, 0.05f);
    }

    [Fact]
    public void Tick_JumpTransition_CostsOnce()
    {
        Walk(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Jumping);
        _rig.Tick(5);

        // One jump: 10 points once, however long the body stays in the air (the regeneration waits for the delay).
        Assert.Equal(90f, Vitals(_rig.Alice).Stamina, 0.05f);
    }

    [Fact]
    public void Tick_Landing15mps_32Damage()
    {
        Walk(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Falling, _fall15);
        _rig.Tick(1);
        Assert.Equal(100f, Vitals(_rig.Alice).Health, 0.05f);

        _rig.Motion.Set(_rig.Alice, MovementMode.Walking);
        _rig.Tick(1);

        Assert.Equal(68f, Vitals(_rig.Alice).Health, 0.05f);
    }

    [Fact]
    public void Tick_Landing_RealSequenceFalling15_Falling0_Walking0_32Damage()
    {
        // The engine zeroes the vertical velocity in the tick of the contact while the mode is still Falling; Walking comes one tick later.
        Walk(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Falling, _fall15);
        _rig.Tick(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Falling, Vector3.Zero);
        _rig.Tick(1);
        Assert.Equal(100f, Vitals(_rig.Alice).Health, 0.05f);

        _rig.Motion.Set(_rig.Alice, MovementMode.Walking);
        _rig.Tick(1);

        Assert.Equal(68f, Vitals(_rig.Alice).Health, 0.05f);
    }

    [Fact]
    public void Tick_LongFall_UsesThePeakSpeedOfTheFall()
    {
        Walk(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Falling, new Vector3(0f, -5f, 0f));
        _rig.Tick(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Falling, _fall15);
        _rig.Tick(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Falling, new Vector3(0f, -12f, 0f));
        _rig.Tick(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Walking);
        _rig.Tick(1);

        Assert.Equal(68f, Vitals(_rig.Alice).Health, 0.05f);
    }

    [Fact]
    public void Tick_LandThenHop_FallChargedOnce()
    {
        // One landing is charged once: after it the peak of the fall is forgotten, so a later small hop does not bring the old 15 m/s back.
        Walk(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Falling, _fall15);
        _rig.Tick(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Walking);
        _rig.Tick(1);
        Assert.Equal(68f, Vitals(_rig.Alice).Health, 0.05f);

        Walk(5);
        _rig.Motion.Set(_rig.Alice, MovementMode.Jumping, new Vector3(0f, 5f, 0f));
        _rig.Tick(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Falling, new Vector3(0f, -5f, 0f));
        _rig.Tick(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Walking);
        _rig.Tick(1);

        // Only the regeneration moved the health since the first landing (a stale peak would have cost another 32).
        Assert.InRange(Vitals(_rig.Alice).Health, 67.9f, 69f);
    }

    [Fact]
    public void Tick_FrozenMidFall_PeakRestarts()
    {
        // A body that is frozen in the middle of a fall (a rescue, a teleport) starts the peak anew: the 20 m/s before the freeze is not charged at the landing.
        Walk(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Falling, new Vector3(0f, -20f, 0f));
        _rig.Tick(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Frozen);
        _rig.Tick(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Falling, new Vector3(0f, -5f, 0f));
        _rig.Tick(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Walking);
        _rig.Tick(1);

        Assert.Equal(100f, Vitals(_rig.Alice).Health, 0.05f);
    }

    [Fact]
    public void Tick_LandingAfterReparent_NoDamage()
    {
        Walk(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Falling, _fall15);
        _rig.Tick(1);

        // The twin above takes 32; here the frame counter changed between the ticks (a teleport or a rescue from under the core): no damage.
        _rig.Reparent(_rig.Alice);
        _rig.Motion.Set(_rig.Alice, MovementMode.Walking);
        _rig.Tick(1);

        Assert.Equal(100f, Vitals(_rig.Alice).Health, 0.05f);
    }

    [Fact]
    public void Tick_Attached_NoFallNoSprintCost()
    {
        Walk(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Sprinting, attached: true);
        _rig.Tick(60);
        Assert.Equal(100f, Vitals(_rig.Alice).Stamina, 0.05f);

        _rig.Motion.Set(_rig.Alice, MovementMode.Falling, _fall15, attached: true);
        _rig.Tick(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Walking, attached: true);
        _rig.Tick(1);

        Assert.Equal(100f, Vitals(_rig.Alice).Health, 0.05f);
    }

    [Fact]
    public void Tick_HealthNeverBelowOne()
    {
        Walk(1);

        _rig.Vitals.ApplyDamage(_rig.Alice, 500f, DamageKind.Fall);
        _rig.Tick(1);

        Assert.Equal(1f, Vitals(_rig.Alice).Health, 0.001f);
    }

    [Fact]
    public void ApplyDamage_ReducesHealth()
    {
        Walk(1);
        _rig.Vitals.ApplyDamage(_rig.Alice, 0f, DamageKind.Fall);
        _rig.Vitals.ApplyDamage(_rig.Alice, float.NaN, DamageKind.Fall);
        _rig.Tick(1);
        Assert.Equal(100f, Vitals(_rig.Alice).Health, 0.05f);

        _rig.Vitals.ApplyDamage(_rig.Alice, 30f, DamageKind.Fall);
        _rig.Tick(1);

        Assert.Equal(70f, Vitals(_rig.Alice).Health, 0.05f);
    }

    [Fact]
    public void TrySpendStamina_NotEnough_FalseAndUnchanged()
    {
        Walk(1);

        Assert.True(_rig.Vitals.TrySpendStamina(_rig.Alice, 40f));
        Assert.Equal(60f, Vitals(_rig.Alice).Stamina, 0.05f);
        Assert.False(_rig.Vitals.TrySpendStamina(_rig.Alice, 61f));
        Assert.False(_rig.Vitals.TrySpendStamina(_rig.Alice, float.NaN));
        Assert.Equal(60f, Vitals(_rig.Alice).Stamina, 0.05f);
    }

    [Fact]
    public void IsExhausted_ZeroUntil20Percent()
    {
        Walk(1);
        _rig.Motion.Set(_rig.Alice, MovementMode.Sprinting);
        _rig.Tick(760);
        Assert.True(_rig.Vitals.IsExhausted(_rig.Alice));

        // Not sprinting any more: the regeneration waits 60 ticks, then 15 per second; 20 percent of 100 is reached about 80 ticks later.
        _rig.Motion.Set(_rig.Alice, MovementMode.Walking);
        _rig.Tick(100);
        Assert.True(_rig.Vitals.IsExhausted(_rig.Alice));
        _rig.Tick(50);
        Assert.False(_rig.Vitals.IsExhausted(_rig.Alice));
    }

    [Fact]
    public void Tick_FoodRaisesMaxima()
    {
        _rig.ReadyBoth();
        _rig.Motion.Set(_rig.Alice, MovementMode.Walking);
        _rig.Tick(6);
        Assert.Equal(100f, _rig.MessagesTo(_rig.Alice)[^1].HealthMax, 0.01f);

        // Berries: 10 health and 20 stamina at full strength (D9), so the maxima rise by that much at once.
        var food = new FoodSlots(new FoodSlot(FoodServerRig.Berries, _rig.Sim.State.CurrentTick), default, default);
        _rig.Sim.ApplyServerOperation(new FoodSetOp(_rig.Alice, food));
        _rig.Tick(6);

        var message = _rig.MessagesTo(_rig.Alice)[^1];
        Assert.Equal(110f, message.HealthMax, 0.05f);
        Assert.Equal(120f, message.StaminaMax, 0.05f);
    }

    [Fact]
    public void Tick_SendsVitalsEvery6Ticks_OnlyToOwner()
    {
        _rig.ReadyBoth();
        _rig.Motion.Set(_rig.Alice, MovementMode.Walking);
        _rig.Motion.Set(_rig.Bob, MovementMode.Walking);
        _rig.Tick(1);
        _rig.Vitals.ApplyDamage(_rig.Bob, 50f, DamageKind.Fall);
        _rig.Sent.Clear();

        _rig.Tick(12);

        // Ticks 1..12: the messages go at the ticks that are multiples of 6 (6 and 12), one per player, each about its owner.
        var alice = _rig.MessagesTo(_rig.Alice);
        var bob = _rig.MessagesTo(_rig.Bob);
        Assert.Equal(2, alice.Count);
        Assert.Equal(2, bob.Count);
        Assert.Equal(4, _rig.Sent.Count);
        Assert.All(alice, m => Assert.Equal(100f, m.Health, 0.05f));
        Assert.All(bob, m => Assert.Equal(50f, m.Health, 0.25f));
    }

    [Fact]
    public void Tick_PlayerNotReady_GetsNothing()
    {
        _rig.Motion.Set(_rig.Alice, MovementMode.Walking);

        _rig.Tick(12);

        Assert.Empty(_rig.Sent);
        Assert.Equal(0, _rig.Vitals.MessagesSent);
    }

    [Fact]
    public void IntentAccepted_TerrainTool_CostsStamina()
    {
        Walk(1);
        var tool = new TerrainToolIntent(WorldFactory.HomePlanet, new Vector3(0f, 4005f, 0f), -Vector3.UnitY, 0.5f, TerrainEditMode.Remove, 0);

        _rig.Vitals.OnIntentAccepted(_rig.Alice, tool);
        _rig.Tick(1);

        Assert.Equal(96f, Vitals(_rig.Alice).Stamina, 0.05f);
    }

    [Fact]
    public void IntentAccepted_PropHitWithoutOperation_CostsStamina()
    {
        Walk(1);

        _rig.Vitals.OnIntentAccepted(_rig.Alice, new PropHitIntent(WorldFactory.HomePlanet, new PropId(1), 0));
        _rig.Tick(1);

        Assert.Equal(96f, Vitals(_rig.Alice).Stamina, 0.05f);
    }

    [Fact]
    public void SteadyTick_DoesNotAllocate()
    {
        // Alice sits by the fire under a roof (the rest is evaluated every 30 ticks), Bob walks; neither is ready, so no message is built.
        _rig.Heat();
        _rig.Seat(_rig.Alice);
        _rig.Motion.Set(_rig.Alice, MovementMode.Walking);
        _rig.Motion.Set(_rig.Bob, MovementMode.Walking);
        _rig.Tick(400);

        var allocated = long.MaxValue;
        for (var round = 0; round < 40 && allocated != 0; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 120; i++)
            {
                _rig.Vitals.Step();
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Assert.Equal(0, allocated);
    }
}
