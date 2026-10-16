using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Simulation;
using Horyzont.Core.Survival;
using Horyzont.Core.Tests.Serialization;
using Horyzont.Testing;

using static Horyzont.Core.Tests.Survival.FoodWorld;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// K6-33: eating does not break the conservation of items. Random sequences of giving food, eating (the compound of D10 built with
/// <see cref="FoodSlotRules"/>), eating that must fail (the destroy finds nothing, the food slots repeat a type), moving stacks, crafting food into other food
/// and the passing of time; after every operation the sum of every item type equals created minus destroyed, the food slots hold no type twice, an empty
/// slot has tick 0, a record of progress exists if and only if a slot is filled, and a refused operation leaves the hash unchanged.
/// </summary>
public sealed class FoodConservationProperties
{
    private const int Sequences = 1000;
    private const int OperationsPerSequence = 40;
    private const ushort FoodLimit = 20;

    /// <summary>Reject reason 49 of the plan; the enum value arrives in step B2.</summary>
    private const RejectReason FoodAlreadyActive = (RejectReason)49;

    private static readonly VitalsRules _rules = VitalsRules.Default;
    private static readonly ItemTypeId[] _foods = [Berries, Jam, Broth, Bread];

    private enum Action
    {
        Give,
        Eat,

        /// <summary>The destroy of the compound finds nothing, the food slots are valid: the whole compound is refused.</summary>
        EatNothing,

        /// <summary>The destroy finds the item, the food slots repeat a type: the whole compound is refused and the item stays.</summary>
        EatDuplicate,
        Move,

        /// <summary>Two berries become one jam (destroy with the sink Craft, create with the source Craft).</summary>
        Craft,
        Advance,
    }

    private static Gen<Step> StepGen { get; } =
        from action in Gen.Frequency(
            (6, Gen.Constant(Action.Give)),
            (9, Gen.Constant(Action.Eat)),
            (2, Gen.Constant(Action.EatNothing)),
            (2, Gen.Constant(Action.EatDuplicate)),
            (3, Gen.Constant(Action.Move)),
            (3, Gen.Constant(Action.Craft)),
            (5, Gen.Constant(Action.Advance)))
        from slot in Gen.Choose(0, 5)
        from slot2 in Gen.Choose(0, 5)
        from food in Gen.Choose(0, _foods.Length - 1)
        from count in Gen.Choose(1, 4)
        from ticks in Gen.Elements(10, 600, 7200, 20000, 40000, 80000)
        select new Step(action, slot, slot2, food, count, ticks);

    private static Gen<Step[]> Sequence { get; } = StepGen.ArrayOf(OperationsPerSequence);

    [Fact]
    public void EatMoveCraft_RandomSequences_ConservationAndFoodInvariants()
    {
        Kit.ForAll(Sequence, sequence => Run(sequence, null), Sequences);

        // A property over sequences in which every operation is refused holds trivially: the generator must also get past the checks.
        AssertGeneratorCoverage();
    }

    [Fact]
    public void Generator_ProducesEatenAndRefusedFood() => AssertGeneratorCoverage();

    private static void AssertGeneratorCoverage()
    {
        var coverage = new Coverage();
        foreach (var sequence in Sequence.Sample(400, 20))
        {
            Run(sequence, coverage);
        }

        // The thresholds are first guesses to be tuned in steps A6 and A7, when the rules run.
        Assert.True(coverage.Eaten >= 20, $"eaten {coverage.Eaten} times");
        Assert.True(coverage.RefusedByRule >= 10, $"refused by the rule (49) {coverage.RefusedByRule} times");
        Assert.True(coverage.BrokenRefused >= 10, $"refused compounds {coverage.BrokenRefused} times");
        Assert.True(coverage.Crafted >= 5, $"crafted {coverage.Crafted} times");
    }

    private static void Run(Step[] sequence, Coverage? coverage)
    {
        var f = new FoodWorld();
        var table = FoodContent.Table(f.State);
        var applier = new OperationApplier(f.State, f.Blocks.Sim.Terrain);
        var expected = new long[SurvivalContentFactory.FoodItemKeys.Count + 1];
        ulong now = 1000;
        foreach (var step in sequence)
        {
            if (step.Action == Action.Advance)
            {
                now += (ulong)step.Ticks;
                continue;
            }

            var op = Build(f, table, step, now, coverage);
            if (op is null)
            {
                continue;
            }

            var hash = StateHasher.Hash(f.State);
            if (applier.TryApply(f.State.LastOpId.Next, f.State.CurrentTick, null, op, out _, out _, out _))
            {
                Assert.NotEqual(Action.EatNothing, step.Action);
                Assert.NotEqual(Action.EatDuplicate, step.Action);
                Account(op, expected);
                coverage?.Accepted(step.Action);
            }
            else
            {
                Assert.Equal(hash, StateHasher.Hash(f.State));
                coverage?.Refused(step.Action);
            }

            Assert.Equal(expected, Totals(f));
            AssertFoodInvariants(f);
        }
    }

    /// <summary>The operation of a step; null when the step has nothing to do (no stack to eat, or the rule refuses the food, which is counted).</summary>
    private static Operation? Build(FoodWorld f, FoodTable table, Step step, ulong now, Coverage? coverage)
    {
        var food = _foods[step.Food];
        var stack = f.BagStack(Player1, step.Slot);
        switch (step.Action)
        {
            case Action.Give:
                return new ItemCreateOp(BagSlot(Player1, step.Slot), food, (ushort)step.Count, FoodLimit, ItemSource.Harvest);
            case Action.Eat:
                if (stack.IsEmpty)
                {
                    return null;
                }

                var reason = FoodSlotRules.TryPlace(f.FoodOf(Player1), stack.Type, table, new Tick(now), _rules, FoodContent.TickRate, out var next);
                if (reason != RejectReason.None)
                {
                    Assert.Equal(FoodAlreadyActive, reason);
                    coverage?.RefuseByRule();
                    return null;
                }

                return EatOp(Player1, step.Slot, stack.Type, next);
            case Action.EatNothing:
                // The last slot of the bag is never filled by this property.
                return EatOp(Player1, 39, food, Slots(Slot(food, now)));
            case Action.EatDuplicate:
                return stack.IsEmpty
                    ? null
                    : EatOp(Player1, step.Slot, stack.Type, Slots(Slot(stack.Type, now), Slot(stack.Type, now + 1)));
            case Action.Move:
                return new ItemTransferOp(BagSlot(Player1, step.Slot), BagSlot(Player1, step.Slot2), food, (ushort)step.Count, FoodLimit);
            default:
                return new CompoundOp(
                [
                    new ItemDestroyOp(BagSlot(Player1, step.Slot), Berries, 2, ItemSink.Craft),
                    new ItemCreateOp(BagSlot(Player1, step.Slot2), Jam, 1, FoodLimit, ItemSource.Craft),
                ]);
        }
    }

    private static void Account(Operation op, long[] expected)
    {
        switch (op)
        {
            case ItemCreateOp create:
                expected[create.Type.Value] += create.Count;
                break;
            case ItemDestroyOp destroy:
                expected[destroy.Type.Value] -= destroy.Count;
                break;
            case CompoundOp compound:
                foreach (var component in compound.Components)
                {
                    Account(component, expected);
                }

                break;
        }
    }

    private static long[] Totals(FoodWorld f)
    {
        var totals = new long[SurvivalContentFactory.FoodItemKeys.Count + 1];
        foreach (var container in f.State.Containers.All)
        {
            foreach (var stack in container.Slots)
            {
                if (!stack.IsEmpty)
                {
                    Assert.InRange(stack.Count, (ushort)1, (ushort)500);
                    totals[stack.Type.Value] += stack.Count;
                }
            }
        }

        return totals;
    }

    /// <summary>No type twice among the filled slots, an empty slot has tick 0, and the record exists if and only if a slot is filled (nothing else is set in this world).</summary>
    private static void AssertFoodInvariants(FoodWorld f)
    {
        var food = f.FoodOf(Player1);
        var seen = new HashSet<ushort>();
        for (var i = 0; i < FoodSlots.Count; i++)
        {
            if (food[i].IsEmpty)
            {
                Assert.Equal(new Tick(0), food[i].Eaten);
            }
            else
            {
                Assert.True(seen.Add(food[i].Type.Value), $"food type {food[i].Type.Value} twice");
                Assert.True(f.State.ItemTypes.IsKnown(food[i].Type));
            }
        }

        Assert.Equal(!food.IsEmpty, f.State.Progress.TryGet(Player1, out _));
    }

    private sealed record Step(Action Action, int Slot, int Slot2, int Food, int Count, int Ticks);

    private sealed class Coverage
    {
        public int Eaten { get; private set; }

        public int RefusedByRule { get; private set; }

        public int BrokenRefused { get; private set; }

        public int Crafted { get; private set; }

        public void RefuseByRule() => RefusedByRule++;

        public void Accepted(Action action)
        {
            Eaten += action == Action.Eat ? 1 : 0;
            Crafted += action == Action.Craft ? 1 : 0;
        }

        public void Refused(Action action) => BrokenRefused += action is Action.EatNothing or Action.EatDuplicate ? 1 : 0;
    }
}
