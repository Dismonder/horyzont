using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.Tests.Serialization;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Items;

/// <summary>
/// The conservation invariant of spec 03: for every item type the sum of counts in all containers equals the created minus the destroyed
/// count; a refused operation (also a compound with a failing component at a random position) leaves the state hash unchanged.
/// </summary>
public sealed class ItemConservationProperties
{
    private const int Sequences = 2000;
    private const int OperationsPerSequence = 50;

    private static readonly ContainerId[] _containers =
        [WorldFactory.BagOf(1), WorldFactory.HotbarOf(1), WorldFactory.BagOf(2), WorldFactory.HotbarOf(2)];

    private static readonly ItemTypeId[] _types = [ItemFixture.Tool, ItemFixture.Gem, ItemFixture.Stone, ItemFixture.Soil];

    private static readonly ushort[] _limits = [1, 5, 500, 500];

    private static Gen<SlotRef> Slot { get; } =
        from container in Gen.Elements(_containers)
        from slot in Gen.Frequency((12, Gen.Choose(0, 2)), (1, Gen.Choose(9, 11)))
        select new SlotRef(container, (ushort)slot);

    private static Gen<int> Type { get; } = Gen.Choose(0, _types.Length - 1);

    private static Gen<ItemTypeId> ExpectedType { get; } = Gen.Frequency((1, Gen.Constant(ItemTypeId.None)), (3, Gen.Elements(_types)));

    private static Gen<Operation> Simple { get; } = Gen.Frequency(
        (4, from slot in Slot
            from type in Type
            from count in Gen.Choose(1, 8)
            select (Operation)new ItemCreateOp(slot, _types[type], (ushort)count, _limits[type], ItemSource.Harvest)),
        (2, from slot in Slot
            from type in Type
            from count in Gen.Choose(1, 2)
            select (Operation)new ItemDestroyOp(slot, _types[type], (ushort)count, ItemSink.Craft)),
        (4, from source in Slot
            from target in Slot
            from type in Type
            from count in Gen.Choose(1, 8)
            select (Operation)new ItemTransferOp(source, target, _types[type], (ushort)count, _limits[type])),
        (2, from a in Slot
            from b in Slot
            from expectedA in ExpectedType
            from expectedB in ExpectedType
            select (Operation)new ItemSwapOp(a, b, expectedA, expectedB)));

    // Slots 20..23 of the bags are touched only by compounds whose core leaves them empty again (create, transfer, destroy), so many
    // compounds are accepted; an optional random component and a destroy of 501 (stacks hold at most 500) at a random position make
    // others fail at every position.
    private static Gen<Operation> Compound { get; } =
        from bag in Gen.Elements(WorldFactory.BagOf(1), WorldFactory.BagOf(2))
        from a in Gen.Choose(20, 23)
        from b in Gen.Choose(20, 23).Where(b => b != a)
        from type in Type
        from count in Gen.Choose(1, 5)
        from extra in Gen.Frequency((2, Gen.Constant<Operation?>(null)), (1, Simple.Select(o => (Operation?)o)))
        from broken in Gen.Choose(-4, 3)
        from slot in Slot
        select (Operation)new CompoundOp(Break(Core(new SlotRef(bag, (ushort)a), new SlotRef(bag, (ushort)b), type, Math.Min(count, _limits[type]), extra), broken, slot));

    private static Gen<Operation[]> Sequence { get; } =
        Gen.Frequency((5, Simple), (2, Compound)).ArrayOf(OperationsPerSequence);

    [Fact]
    public void RandomSequences_ConserveItems_AndRefusedOperationsChangeNothing() =>
        Kit.ForAll(Sequence, Run, Sequences);

    [Fact]
    public void Generator_ProducesAcceptedAndRefusedOperationsOfEveryKind()
    {
        var accepted = new Dictionary<OperationKind, int>();
        var refused = new Dictionary<OperationKind, int>();
        foreach (var sequence in Sequence.Sample(100, 20))
        {
            var f = new ItemFixture(players: 2);
            var applier = new OperationApplier(f.State, f.Sim.Terrain);
            foreach (var op in sequence)
            {
                var outcome = applier.TryApply(f.State.LastOpId.Next, f.State.CurrentTick, null, op, out _, out _, out _) ? accepted : refused;
                outcome[op.Kind] = outcome.GetValueOrDefault(op.Kind) + 1;
            }
        }

        foreach (var kind in new[] { OperationKind.Compound, OperationKind.ItemCreate, OperationKind.ItemDestroy, OperationKind.ItemTransfer, OperationKind.ItemSwap })
        {
            Assert.True(accepted.GetValueOrDefault(kind) >= 10, $"{kind} accepted {accepted.GetValueOrDefault(kind)} times");
            Assert.True(refused.GetValueOrDefault(kind) >= 10, $"{kind} refused {refused.GetValueOrDefault(kind)} times");
        }
    }

    private static void Run(Operation[] sequence)
    {
        var f = new ItemFixture(players: 2);
        var applier = new OperationApplier(f.State, f.Sim.Terrain);
        var expected = new long[_types.Length + 1];
        foreach (var op in sequence)
        {
            var hash = StateHasher.Hash(f.State);
            if (applier.TryApply(f.State.LastOpId.Next, f.State.CurrentTick, null, op, out _, out _, out _))
            {
                Account(op, expected);
            }
            else
            {
                Assert.Equal(hash, StateHasher.Hash(f.State));
            }

            Assert.Equal(expected, Totals(f));
        }
    }

    private static Operation[] Core(SlotRef a, SlotRef b, int type, int count, Operation? extra)
    {
        Operation[] core =
        [
            new ItemCreateOp(a, _types[type], (ushort)count, _limits[type], ItemSource.Harvest),
            new ItemTransferOp(a, b, _types[type], (ushort)count, _limits[type]),
            new ItemDestroyOp(b, _types[type], (ushort)count, ItemSink.Craft),
        ];
        return extra is null ? core : [.. core, extra];
    }

    private static Operation[] Break(Operation[] parts, int broken, SlotRef slot)
    {
        if (broken >= 0 && broken < parts.Length)
        {
            parts[broken] = new ItemDestroyOp(slot, ItemFixture.Stone, 501, ItemSink.Craft);
        }

        return parts;
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

    private static long[] Totals(ItemFixture f)
    {
        var totals = new long[_types.Length + 1];
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
}
