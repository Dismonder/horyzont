using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Content;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Survival;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.Tests.Serialization;

using static Horyzont.Core.Tests.Survival.HazardWorld;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// K6-34: the suit refills, the equipment and the oxygen generator do not break the conservation of items, and the generator burns what a tick-by-tick model burns.
/// <para>
/// The first property runs random sequences of giving items, refilling the suit, moving modules into the equipment, feeding and emptying the generator input, settling and the passing of time through
/// <see cref="Horyzont.Core.Simulation.ServerSimulation.Submit"/>; after every step the sum of every item type equals created minus destroyed in the journal, the equipment holds only what its slot accepts and
/// the generator state matches its input. Fuel destroyed by the generator is a legal outflow, so that property alone cannot tell burning from theft (review B1, P2-2a).
/// </para>
/// <para>
/// The second property does: random events (put cells in, take cells out, settle, let time pass) on one generator, with the pure functions of <see cref="OxygenGeneratorProcess"/> driven the way the simulation
/// drives them (settle before every change of the input, settle after), against a reference that moves tick by tick. Both the total of burned cells and "does it run" at sampled ticks must agree.
/// Two mutants of the driver (no settlement before, no settlement after) must be caught, so the property can fail.
/// </para>
/// </summary>
public sealed class HazardConservationProperties
{
    private const int Sequences = 150;
    private const int OperationsPerSequence = 40;
    private const int ModelSequences = 400;
    private const int ModelEvents = 30;
    private const ulong Per = 600;

    private enum Action
    {
        Give,
        Refill,
        Equip,
        Feed,
        TakeOut,
        Settle,
        Advance,
    }

    private static readonly ItemTypeId[] _givable = [HeatCell, Canister, Fuel, Tank, Thermal, Stone];

    private static Gen<Step> StepGen { get; } =
        from action in Gen.Frequency(
            (6, Gen.Constant(Action.Give)),
            (7, Gen.Constant(Action.Refill)),
            (5, Gen.Constant(Action.Equip)),
            (6, Gen.Constant(Action.Feed)),
            (4, Gen.Constant(Action.TakeOut)),
            (3, Gen.Constant(Action.Settle)),
            (6, Gen.Constant(Action.Advance)))
        from slot in Gen.Choose(0, 5)
        from slot2 in Gen.Choose(0, 6)
        from item in Gen.Choose(0, _givable.Length - 1)
        from count in Gen.Choose(1, 4)
        from ticks in Gen.Elements(30, 300, 600, 1000, 2500)
        select new Step(action, slot, slot2, item, count, ticks);

    private static Gen<Step[]> Sequence { get; } = StepGen.ArrayOf(OperationsPerSequence);

    [Fact]
    public void MoveRefillFeedSettle_RandomSequences_ConservationAndGeneratorIntegrity()
    {
        var coverage = new Coverage();
        Kit.ForAll(Sequence, sequence => Run(sequence, coverage), Sequences);

        // A property over sequences in which every step is refused holds trivially: the generator must also get past the checks.
        Assert.True(coverage.Refills >= 20, $"refills accepted {coverage.Refills} times");
        Assert.True(coverage.BurningSettlements >= 10, $"settlements that burned cells {coverage.BurningSettlements} times");
        Assert.True(coverage.EquipmentRefusals >= 10, $"equipment moves refused {coverage.EquipmentRefusals} times");
    }

    private static void Run(Step[] sequence, Coverage coverage)
    {
        var w = new HazardWorld(survival: true);
        var input = w.PlaceByIntent(PlacedCell);
        var expected = new Dictionary<ushort, long>();
        var accounted = 0;
        Account(w, expected, ref accounted);
        foreach (var step in sequence)
        {
            w.AdvanceTicks(3);
            switch (step.Action)
            {
                case Action.Give:
                    Give(w, step);
                    break;
                case Action.Refill:
                    SubmitRefill(w, step, coverage);
                    break;
                case Action.Equip:
                    SubmitEquip(w, step, coverage);
                    break;
                case Action.Feed:
                    SubmitFeed(w, step, input);
                    break;
                case Action.TakeOut:
                    SubmitTakeOut(w, step, input);
                    break;
                case Action.Settle:
                    var before = w.Blocks.Sim.Journal.Entries.Count;
                    if (w.Simulation.TrySettleStation(new BlockRef(FunctionalBlockFixture.ConstructId, PlacedCell)) is { } applied)
                    {
                        coverage.Settled(applied.Operation);
                    }

                    Assert.True(w.Blocks.Sim.Journal.Entries.Count >= before);
                    break;
                default:
                    w.AdvanceTicks(step.Ticks);
                    break;
            }

            Account(w, expected, ref accounted);
            AssertTotals(w, expected);
            AssertEquipment(w);
            AssertGenerator(w, input);
        }
    }

    private static void Give(HazardWorld w, Step step)
    {
        var type = _givable[step.Item];
        var isModule = type == Tank || type == Thermal;
        var stack = w.Blocks.Container(BagOf(Player1)).GetSlot(step.Slot);
        if (!stack.IsEmpty && stack.Type != type)
        {
            return;
        }

        var count = isModule ? 1 : step.Count;
        if (isModule && !stack.IsEmpty)
        {
            return;
        }

        w.Give(Player1, type, count, step.Slot);
    }

    private static void SubmitRefill(HazardWorld w, Step step, Coverage coverage)
    {
        var hash = StateHasher.Hash(w.State);

        var result = w.Simulation.Submit(Player1, new SuitRefillIntent(BagOf(Player1), (ushort)step.Slot));

        if (result.IsAccepted)
        {
            coverage.Refills++;
        }
        else
        {
            Assert.Equal(hash, StateHasher.Hash(w.State));
        }
    }

    private static void SubmitEquip(HazardWorld w, Step step, Coverage coverage)
    {
        var stack = w.Blocks.Container(BagOf(Player1)).GetSlot(step.Slot);
        if (stack.IsEmpty)
        {
            return;
        }

        var hash = StateHasher.Hash(w.State);
        var move = new ItemMoveIntent(BagSlot(Player1, step.Slot), ItemMoveTarget.ToSlot(new SlotRef(EquipmentOf(Player1), (ushort)step.Slot2)), 1, stack.Type, stack.Count);

        var result = w.Simulation.Submit(Player1, move);

        if (!result.IsAccepted)
        {
            Assert.Equal(hash, StateHasher.Hash(w.State));
            coverage.EquipmentRefusals++;
        }
    }

    private static void SubmitFeed(HazardWorld w, Step step, ContainerId input)
    {
        var stack = w.Blocks.Container(BagOf(Player1)).GetSlot(step.Slot);
        if (stack.IsEmpty)
        {
            return;
        }

        w.Simulation.Submit(Player1, new ItemMoveIntent(BagSlot(Player1, step.Slot), ItemMoveTarget.AnyIn(input), stack.Count, stack.Type, stack.Count));
    }

    private static void SubmitTakeOut(HazardWorld w, Step step, ContainerId input)
    {
        if (step.Slot % 2 == 0)
        {
            w.Simulation.Submit(Player1, new TakeAllIntent(input));
            return;
        }

        var stack = w.Blocks.Container(input).GetSlot(step.Slot % 2);
        if (!stack.IsEmpty)
        {
            w.Simulation.Submit(Player1, new ItemMoveIntent(new SlotRef(input, (ushort)(step.Slot % 2)), ItemMoveTarget.AnyIn(BagOf(Player1)), stack.Count, stack.Type, stack.Count));
        }
    }

    /// <summary>Adds the journal entries since the last call to the expected totals (created minus destroyed).</summary>
    private static void Account(HazardWorld w, Dictionary<ushort, long> expected, ref int accounted)
    {
        var entries = w.Blocks.Sim.Journal.Entries;
        for (; accounted < entries.Count; accounted++)
        {
            Account(entries[accounted].Operation, expected);
        }
    }

    private static void Account(Operation op, Dictionary<ushort, long> expected)
    {
        switch (op)
        {
            case ItemCreateOp create:
                expected[create.Type.Value] = expected.GetValueOrDefault(create.Type.Value) + create.Count;
                break;
            case ItemDestroyOp destroy:
                expected[destroy.Type.Value] = expected.GetValueOrDefault(destroy.Type.Value) - destroy.Count;
                break;
            case CompoundOp compound:
                foreach (var component in compound.Components)
                {
                    Account(component, expected);
                }

                break;
        }
    }

    private static void AssertTotals(HazardWorld w, Dictionary<ushort, long> expected)
    {
        var totals = new Dictionary<ushort, long>();
        foreach (var container in w.State.Containers.All)
        {
            foreach (var stack in container.Slots)
            {
                if (!stack.IsEmpty)
                {
                    totals[stack.Type.Value] = totals.GetValueOrDefault(stack.Type.Value) + stack.Count;
                }
            }
        }

        foreach (var (type, count) in expected.Where(e => e.Value != 0))
        {
            Assert.Equal(count, totals.GetValueOrDefault(type));
        }

        foreach (var (type, count) in totals)
        {
            Assert.Equal(expected.GetValueOrDefault(type), count);
        }
    }

    /// <summary>Every worn item sits in a slot of the layout that accepts it (the modules of the test content go to slots 4 to 6 only).</summary>
    private static void AssertEquipment(HazardWorld w)
    {
        var slots = w.EquipmentOfPlayer(Player1);
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].IsEmpty)
            {
                continue;
            }

            Assert.True(slots[i].Type == Tank || slots[i].Type == Thermal, $"slot {i} holds type {slots[i].Type.Value}");
            Assert.True(EquipmentLayout.Accepts(i, EquipmentSlot.Module), $"slot {i} does not accept a module");
            Assert.Equal(1, slots[i].Count);
        }
    }

    /// <summary>The generator state exists, points at its input and never starts in the future.</summary>
    private static void AssertGenerator(HazardWorld w, ContainerId input)
    {
        var state = w.GeneratorAt(PlacedCell);
        Assert.NotNull(state);
        Assert.Equal(input, state.Input);
        Assert.True(w.Blocks.Container(input).Kind == ContainerKind.StationInput);
        Assert.True(state.Started is null || state.Started.Value.Value <= w.Now.Value);
    }

    // ---- The generator against a tick-by-tick model ----

    private enum EventKind
    {
        Insert,
        Take,
        Settle,
        Advance,
    }

    private sealed record Event(EventKind Kind, int Count, int Ticks);

    private static Gen<Event[]> Events { get; } =
        (from kind in Gen.Frequency((4, Gen.Constant(EventKind.Insert)), (3, Gen.Constant(EventKind.Take)), (2, Gen.Constant(EventKind.Settle)), (6, Gen.Constant(EventKind.Advance)))
         from count in Gen.Choose(1, 3)
         from ticks in Gen.Elements(1, 59, 599, 600, 601, 1199, 1200, 3000)
         select new Event(kind, count, ticks)).ArrayOf(ModelEvents);

    [Fact]
    public void GeneratorSettlements_MatchTickByTickReference()
    {
        Kit.ForAll(Events, events => Assert.Null(RunModel(events)), ModelSequences);
    }

    [Fact]
    public void GeneratorSettlements_Mutants_AreCaught()
    {
        // The property can fail: dropping the settlement before a change of the input, or the one after it, is caught on some sequence (the free cells of review B1, P2-2).
        var sequences = Events.Sample(ModelSequences, 7);
        Assert.Contains(sequences, s => RunModel(s, settleBefore: false) is not null);
        Assert.Contains(sequences, s => RunModel(s, settleAfter: false) is not null);
        Assert.All(sequences, s => Assert.Null(RunModel(s)));
    }

    /// <summary>Runs the events through the driver and the reference; returns the first disagreement, null when they agree.</summary>
    private static string? RunModel(Event[] events, bool settleBefore = true, bool settleAfter = true)
    {
        // Driver: what the world stores.
        var queue = 0;
        Tick? started = null;
        var burnedDriver = 0;
        var now = 1000UL;

        // Reference: tick by tick.
        var refQueue = 0;
        var refBurning = false;
        var refEnd = 0UL;
        var burnedRef = 0;

        void Settle()
        {
            var s = OxygenGeneratorProcess.Settle(queue, Per, started, new Tick(now));
            queue -= s.Burned;
            burnedDriver += s.Burned;
            started = s.NextStarted;
        }

        void RefProcess()
        {
            if (refBurning && now >= refEnd)
            {
                refBurning = false;
            }

            if (!refBurning && refQueue > 0)
            {
                refQueue--;
                burnedRef++;
                refBurning = true;
                refEnd = now + Per;
            }
        }

        string? Compare(string where)
        {
            var running = OxygenGeneratorProcess.IsRunning(queue, Per, started, new Tick(now));
            return running == refBurning ? null : $"{where}: tick {now} driver running {running}, reference {refBurning}";
        }

        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case EventKind.Advance:
                    for (var i = 0; i < e.Ticks; i++)
                    {
                        now++;
                        RefProcess();
                        if ((i % 13 == 0 || i == e.Ticks - 1) && Compare("advance") is { } failure)
                        {
                            return failure;
                        }
                    }

                    break;
                case EventKind.Settle:
                    Settle();
                    break;
                case EventKind.Insert:
                    if (settleBefore)
                    {
                        Settle();
                    }

                    queue += e.Count;
                    refQueue += e.Count;
                    RefProcess();
                    if (settleAfter)
                    {
                        Settle();
                    }

                    break;
                default:
                    if (settleBefore)
                    {
                        Settle();
                    }

                    var taken = Math.Min(e.Count, queue);
                    queue -= taken;
                    refQueue = Math.Max(0, refQueue - e.Count);
                    if (settleAfter)
                    {
                        Settle();
                    }

                    break;
            }

            if (Compare(e.Kind.ToString()) is { } disagreement)
            {
                return disagreement;
            }
        }

        // A last settlement: the burned totals must agree, wherever the queue stands.
        Settle();
        return burnedDriver == burnedRef && queue == refQueue ? null : $"end: burned {burnedDriver} vs {burnedRef}, queue {queue} vs {refQueue}";
    }

    private sealed record Step(Action Action, int Slot, int Slot2, int Item, int Count, int Ticks);

    private sealed class Coverage
    {
        public int Refills { get; set; }

        public int EquipmentRefusals { get; set; }

        public int BurningSettlements { get; private set; }

        public void Settled(Operation op)
        {
            if (op is CompoundOp compound && compound.Components.Any(c => c is ItemDestroyOp { Sink: ItemSink.Fuel }))
            {
                BurningSettlements++;
            }
        }
    }
}
