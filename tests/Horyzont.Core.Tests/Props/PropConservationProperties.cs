using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.Hashing;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.Items;
using Horyzont.Core.Tests.Serialization;

using static Horyzont.Core.Tests.Props.PropFixture;

namespace Horyzont.Core.Tests.Props;

/// <summary>
/// K6-31: after every operation the harvested set equals a model set, the items are conserved (sum of counts = created - destroyed) and a
/// refused operation, also a compound with a failing component at a random position, leaves the state hash unchanged.
/// </summary>
public sealed class PropConservationProperties
{
    private const int Sequences = 2000;
    private const int OperationsPerSequence = 50;
    private const int Props = 8;

    private static Gen<Operation> PropComponent { get; } =
        from n in Gen.Choose(0, Props - 1)
        from harvest in Gen.Elements(true, false)
        select harvest ? (Operation)Harvest(Tree(n), (ulong)(100 + n)) : Regrow(Tree(n));

    // Items are only created (1..5 stone into one of 30 bag slots, 50 operations cannot overflow a stack of 500), a destroy of 501 breaks a
    // compound at a random position.
    private static Gen<Operation> Compound { get; } =
        from length in Gen.Choose(1, 4)
        from props in PropComponent.ArrayOf(length)
        from slot in Gen.Choose(0, 29)
        from count in Gen.Choose(1, 5)
        from itemAt in Gen.Choose(0, 4)
        from broken in Gen.Choose(-3, 4)
        select (Operation)new CompoundOp(Assemble(props, slot, count, itemAt, broken));

    private static Gen<Operation[]> Sequence { get; } =
        Gen.Frequency((5, PropComponent), (3, Compound)).ArrayOf(OperationsPerSequence);

    [Fact]
    public void RandomSequences_KeepModelAndConserveItems_AndRefusedOperationsChangeNothing() =>
        Kit.ForAll(Sequence, Run, Sequences);

    [Fact]
    public void Generator_ProducesAcceptedAndRefusedOperationsOfEveryKind()
    {
        var accepted = new Dictionary<OperationKind, int>();
        var refused = new Dictionary<OperationKind, int>();
        foreach (var sequence in Sequence.Sample(400, 20))
        {
            var f = new PropFixture();
            foreach (var op in sequence)
            {
                var outcome = f.TryApplyRaw(op, out _, out _, out _) ? accepted : refused;
                outcome[op.Kind] = outcome.GetValueOrDefault(op.Kind) + 1;
            }
        }

        foreach (var kind in new[] { OperationKind.Compound, (OperationKind)47, (OperationKind)48 })
        {
            Assert.True(accepted.GetValueOrDefault(kind) >= 10, $"{kind} accepted {accepted.GetValueOrDefault(kind)} times");
            Assert.True(refused.GetValueOrDefault(kind) >= 10, $"{kind} refused {refused.GetValueOrDefault(kind)} times");
        }
    }

    private static void Run(Operation[] sequence)
    {
        var f = new PropFixture(players: 1);
        var model = new SortedDictionary<ulong, ulong>();
        long created = 0;
        foreach (var op in sequence)
        {
            var hash = StateHasher.Hash(f.State);
            var expectedOk = TryModel(op, model, out var next, out var items);

            var ok = f.TryApplyRaw(op, out _, out _, out _);

            Assert.Equal(expectedOk, ok);
            if (ok)
            {
                model = next!;
                created += items;
            }
            else
            {
                Assert.Equal(hash, StateHasher.Hash(f.State));
            }

            Assert.Equal(model.Select(p => (p.Key, p.Value)), f.State.HarvestedProps.All.Select(r => (r.Prop.Value, r.Tick.Value)));
            Assert.Equal(created, Items(f));
        }
    }

    /// <summary>The model: a set of (prop, harvest tick); an operation applies component by component to a copy, all or nothing.</summary>
    private static bool TryModel(Operation op, SortedDictionary<ulong, ulong> model, out SortedDictionary<ulong, ulong>? next, out long items)
    {
        var copy = new SortedDictionary<ulong, ulong>(model);
        items = 0;
        next = null;
        foreach (var part in op is CompoundOp compound ? compound.Components : [op])
        {
            switch (part)
            {
                case PropHarvestOp harvest:
                    if (!copy.TryAdd(harvest.Prop.Value, harvest.Tick.Value))
                    {
                        return false;
                    }

                    break;
                case PropRegrowOp regrow:
                    if (!copy.Remove(regrow.Prop.Value))
                    {
                        return false;
                    }

                    break;
                case ItemCreateOp create:
                    items += create.Count;
                    break;
                case ItemDestroyOp:
                    return false;
            }
        }

        next = copy;
        return true;
    }

    private static Operation[] Assemble(IEnumerable<Operation> props, int slot, int count, int itemAt, int broken)
    {
        var parts = props.ToList();
        parts.Insert(Math.Min(itemAt, parts.Count), ItemFixture.Create(ItemFixture.BagSlot(slot), ItemFixture.Stone, count, source: ItemSource.Harvest));
        if (broken >= 0 && broken <= parts.Count)
        {
            parts.Insert(broken, ItemFixture.Destroy(ItemFixture.BagSlot(slot), ItemFixture.Stone, 501));
        }

        return [.. parts];
    }

    private static long Items(PropFixture f)
    {
        long total = 0;
        foreach (var container in f.State.Containers.All)
        {
            foreach (var stack in container.Slots)
            {
                total += stack.IsEmpty ? 0 : stack.Count;
            }
        }

        return total;
    }
}
