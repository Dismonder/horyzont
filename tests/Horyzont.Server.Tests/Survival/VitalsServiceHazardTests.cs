using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Simulation;
using Horyzont.Core.Survival;
using Horyzont.Server.Survival;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Tests.Survival;

/// <summary>
/// <see cref="VitalsService"/> with the hazard content (K6-34 D16 to D18): the suit starts full, drains at the rates of the profile of the planet (the rig is a moon with a vacuum of 0.5 a second unless said otherwise),
/// loses health at zero (10 a second for the oxygen, never below 1), keeps a refill that arrives between two ticks, follows the modules, clamps the intensity of the world, and sends the suit tail of message 44
/// to the owner only. Without the hazard content nothing of this exists (K6-33 behaviour).
/// </summary>
public sealed class VitalsServiceHazardTests
{
    private const string Moon = "core:barren_moon";
    private const int OxygenSlot = 0;

    private static readonly SuitResources _full = new(100f, 100f, 100f, 100f, 100f);

    private static SuitResources WithOxygen(float oxygen) => _full with { Oxygen = oxygen };

    private static HazardServerRig MoonRig(GameRules? rules = null, ILogger<VitalsService>? logger = null)
    {
        var rig = new HazardServerRig(Moon, rules, logger: logger);
        rig.Walk(both: true);
        return rig;
    }

    [Fact]
    public void FreshSuit_FilledToCapacity()
    {
        var rig = new HazardServerRig();
        rig.Walk(both: true);
        rig.Equip(rig.Bob, 4, HazardServerRig.Tank);
        Assert.Null(rig.VitalsOf(rig.Alice));

        rig.Tick(1);

        // The temperate profile drains a little cold at this time of day; every other resource is at its capacity: 100, and 200 oxygen with the tank in a module slot.
        var alice = rig.SuitOf(rig.Alice);
        Assert.True(alice.Heating > 99.9f && alice.Cooling == 100f && alice.Filter == 100f && alice.Shield == 100f && alice.Oxygen == 100f, alice.ToString());
        var bob = rig.SuitOf(rig.Bob);
        Assert.Equal(200f, bob.Oxygen);
        Assert.Equal(100f, bob.Filter);
    }

    [Fact]
    public void MoonSurface_OxygenDrainsThenVacuumDamage()
    {
        var rig = MoonRig();
        rig.SetSuit(rig.Alice, WithOxygen(1f));

        // One point of oxygen lasts 120 ticks at 0.5 a second: until then no damage at all.
        rig.Tick(60);
        Assert.InRange(rig.SuitOf(rig.Alice).Oxygen, 0.45f, 0.55f);
        Assert.Equal(100f, rig.VitalsOf(rig.Alice)!.Value.Health, 0.01f);

        // Then 10 health a second for the 60 ticks that follow.
        rig.Tick(120);
        Assert.Equal(0f, rig.SuitOf(rig.Alice).Oxygen);
        Assert.InRange(rig.VitalsOf(rig.Alice)!.Value.Health, 85f, 95f);
    }

    [Fact]
    public void HealthFloorOne_WithHazards()
    {
        var rig = MoonRig();
        rig.SetSuit(rig.Alice, WithOxygen(0f), health: 5f);

        // 300 ticks of 10 a second would take 50 health: the health stops at 1 until the death of K6-35 (K6-33 D16).
        rig.Tick(300);

        Assert.Equal(1f, rig.VitalsOf(rig.Alice)!.Value.Health, 0.01f);
    }

    [Fact]
    public void FrozenAfterSpawn_SuitSuspended_NoDamage()
    {
        var rig = MoonRig();
        rig.SetSuit(rig.Alice, WithOxygen(0f));
        rig.Motion.Set(rig.Alice, Horyzont.Core.Motion.MovementMode.Frozen);

        // The body waits for the terrain (after a spawn or a rescue): the suit neither drains nor hurts until it moves again (review C-D, P2-1).
        rig.Tick(120);

        Assert.Equal(100f, rig.VitalsOf(rig.Alice)!.Value.Health, 0.01f);
        Assert.Equal(0f, rig.SuitOf(rig.Alice).Oxygen);
        rig.SetSuit(rig.Alice, WithOxygen(50f));
        rig.Tick(120);
        Assert.Equal(50f, rig.SuitOf(rig.Alice).Oxygen);

        // The twin that walks is hurt.
        rig.SetSuit(rig.Alice, WithOxygen(0f));
        rig.Motion.Set(rig.Alice, Horyzont.Core.Motion.MovementMode.Walking);
        rig.Tick(120);
        Assert.True(rig.VitalsOf(rig.Alice)!.Value.Health < 100f);
    }

    [Fact]
    public void ModuleRemoved_ResourceClamped()
    {
        var rig = new HazardServerRig();
        rig.Walk();
        rig.Equip(rig.Alice, 4, HazardServerRig.Tank);
        rig.Tick(1);
        Assert.Equal(200f, rig.SuitOf(rig.Alice).Oxygen);

        rig.Unequip(rig.Alice, 4, HazardServerRig.Tank);
        rig.Tick(1);

        // The module is gone: the capacity is 100 again and the surplus is lost at the next tick (D13).
        Assert.Equal(100f, rig.SuitOf(rig.Alice).Oxygen);
    }

    [Fact]
    public void ModuleRemovedAboveCapacity_MessageSentWithoutThrow()
    {
        var rig = MoonRig();
        rig.ReadyBoth();
        rig.SetSuit(rig.Alice, WithOxygen(150f));

        // 150 of 100 is a legal state until the next tick (a module taken off after a refill, review B5 P2-1): the message of that tick is sent with the value clamped to the capacity of the same step,
        // and the codec, which refuses a value above its capacity, writes it.
        rig.Tick(1);

        var message = Assert.Single(rig.MessagesTo(rig.Alice));
        Assert.Equal(100f, message.Capacity.Oxygen);
        Assert.InRange(message.Suit.Oxygen, 99.9f, 99.995f);
        Assert.Equal(message.Suit.Oxygen, rig.SuitOf(rig.Alice).Oxygen);
        var writer = new ByteWriter();
        MessageCodec.Write(writer, message);
        Assert.Equal(message, MessageCodec.Read(writer.WrittenSpan));
    }

    [Fact]
    public void RefillThenTick_SameTick_KeepsRefill()
    {
        var rig = MoonRig();
        rig.SetSuit(rig.Alice, WithOxygen(30f));
        rig.Give(rig.Alice, OxygenSlot, HazardServerRig.Canister);
        rig.Tick(1);
        var before = rig.SuitOf(rig.Alice).Oxygen;
        Assert.InRange(before, 29.9f, 29.995f);

        Assert.True(rig.Refill(rig.Alice, OxygenSlot).IsAccepted);
        rig.Vitals.Step();

        // The canister gives 50 and the one tick drains 0.5 / 60: the step reads the suit as the refill left it.
        Assert.InRange(rig.SuitOf(rig.Alice).Oxygen, before + 50f - 0.02f, before + 50f);
    }

    [Fact]
    public void RefillBetweenSteps_NotOverwrittenByTheStep()
    {
        var rig = MoonRig();
        rig.SetSuit(rig.Alice, WithOxygen(30f));
        rig.Give(rig.Alice, OxygenSlot, HazardServerRig.Canister);
        rig.Tick(10);
        var before = rig.SuitOf(rig.Alice).Oxygen;
        Assert.InRange(before, 29.8f, 29.95f);

        // The refill is accepted in tick N (between the step of N and its advance); the step of N + 1 must continue from 80, not from the value it saw before (review B5 P2-2).
        Assert.True(rig.Refill(rig.Alice, OxygenSlot).IsAccepted);
        rig.Sim.AdvanceTick();
        rig.Tick(1);

        Assert.InRange(rig.SuitOf(rig.Alice).Oxygen, before + 50f - 0.03f, before + 50f);
    }

    [Fact]
    public void IntensityOutOfRange_LoggedOncePerInstance()
    {
        var first = new ListLogger<VitalsService>();
        var rig = MoonRig(new GameRules { HazardIntensity = 5f }, first);
        rig.SetSuit(rig.Alice, WithOxygen(100f));

        rig.Tick(60);

        // The intensity 5 counts as 2 (the largest): one second at 0.5 a second drains one point; the warning is written once, however many ticks follow.
        Assert.Equal(99f, rig.SuitOf(rig.Alice).Oxygen, 0.02f);
        Assert.Single(first.Entries, e => e.EventId.Id == 8604);

        // A second service has its own flag (no static state).
        var second = new ListLogger<VitalsService>();
        var other = MoonRig(new GameRules { HazardIntensity = 0.1f }, second);
        other.SetSuit(other.Alice, WithOxygen(100f));
        other.Tick(60);
        Assert.Equal(99.75f, other.SuitOf(other.Alice).Oxygen, 0.02f);
        Assert.Single(second.Entries, e => e.EventId.Id == 8604);
    }

    [Fact]
    public void NaNIntensity_FallsBackToOne()
    {
        var rig = MoonRig(new GameRules { HazardIntensity = float.NaN });
        rig.SetSuit(rig.Alice, WithOxygen(100f));

        rig.Tick(60);

        // A NaN would make HazardField return zero rates and switch the hazards off without a trace: it means 1.
        Assert.Equal(99.5f, rig.SuitOf(rig.Alice).Oxygen, 0.02f);
    }

    [Fact]
    public void Message44_CarriesSuitTail_OnlyToOwner()
    {
        var rig = MoonRig();
        rig.ReadyBoth();
        rig.SetSuit(rig.Alice, WithOxygen(50f));
        rig.SetSuit(rig.Bob, WithOxygen(70f));

        rig.Tick(7);

        var alice = rig.MessagesTo(rig.Alice);
        var bob = rig.MessagesTo(rig.Bob);
        Assert.NotEmpty(alice);
        Assert.NotEmpty(bob);
        Assert.All(alice, m => Assert.InRange(m.Suit.Oxygen, 49f, 50f));
        Assert.All(bob, m => Assert.InRange(m.Suit.Oxygen, 69f, 70f));
        var last = alice[^1];
        Assert.Equal(new SuitResources(100f, 100f, 100f, 100f, 100f), last.Capacity);

        // The moon by day has heat, radiation and vacuum, by night cold and vacuum; the vacuum is always there, and nothing protects against it in the open (bit 4, mask 16).
        Assert.True((last.Environment.Present & 0x10) != 0);
        Assert.True((last.Environment.Active & 0x10) != 0);
        Assert.Equal(0, last.Environment.Exposed);
        Assert.False(last.Environment.Sheltered);
        Assert.False(last.Environment.Generator);
        Assert.False(last.Environment.LifeSupport);
    }

    [Fact]
    public void WithoutHazardContent_SuitNullAndDefaultTail()
    {
        var rig = new FoodServerRig();
        rig.ReadyBoth();
        rig.Motion.Set(rig.Alice, Horyzont.Core.Motion.MovementMode.Walking);

        rig.Tick(8);

        Assert.Null(rig.VitalsOf(rig.Alice)!.Value.Suit);
        var message = rig.MessagesTo(rig.Alice)[^1];
        Assert.Equal(PlayerVitalsMessage.DefaultSuit, message.Suit);
        Assert.Equal(PlayerVitalsMessage.DefaultSuit, message.Capacity);
        Assert.Equal(VitalsEnvironment.None, message.Environment);
    }

    [Fact]
    public void SteadyTickWithHazards_DoesNotAllocate()
    {
        var rig = MoonRig();
        rig.Equip(rig.Alice, 4, HazardServerRig.Tank);
        rig.Tick(30);

        // Isolated allocations of the JIT are not recurring: the smallest of several windows must be zero (the players are not ready, so no message is built).
        var smallest = long.MaxValue;
        for (var window = 0; window < 5 && smallest != 0; window++)
        {
            long allocated = 0;
            for (var i = 0; i < 60; i++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                rig.Vitals.Step();
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                rig.Sim.AdvanceTick();
            }

            smallest = Math.Min(smallest, allocated);
        }

        Assert.Equal(0, smallest);
        Assert.InRange(rig.SuitOf(rig.Alice).Oxygen, 150f, 199.9f);
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(EventId EventId, LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((eventId, logLevel, formatter(state, exception)));
    }
}
