using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Tests.Serialization;
using Horyzont.Core.World;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// K6-35: no death and no recovery loses or makes an item. Random sequences of giving items, dying under each penalty, taking all from a tombstone and moving one stack
/// out of it, with several players; after every step the sum of every item type over every container of the world equals what was given, every tombstone is private and
/// belongs to an entity of the same owner in a planet frame, no tombstone is empty, and a refused intent leaves the hash unchanged. Thresholds guard against a property that
/// passes because the generator never gets to the interesting steps.
/// </summary>
public sealed class DeathConservationProperties
{
    private const int Players = 3;
    private const int OperationsPerSequence = 40;
    private const int Sequences = 150;
    private const int CoverageSequences = 300;

    private static readonly ItemTypeId[] _types = [DeathWorld.Stone, DeathWorld.Gem, DeathWorld.Tool, DeathWorld.Tank, DeathWorld.Thermal];

    private enum Action
    {
        Give,
        Die,
        TakeAll,
        MoveOne,

        /// <summary>Fills the bag and the hotbar of the player with tools but a few slots, so that a take-all can only take part.</summary>
        Crowd,
    }

    private static Gen<Step> StepGen { get; } =
        from action in Gen.Frequency(
            (12, Gen.Constant(Action.Give)),
            (4, Gen.Constant(Action.Die)),
            (4, Gen.Constant(Action.TakeAll)),
            (4, Gen.Constant(Action.MoveOne)),
            (1, Gen.Constant(Action.Crowd)))
        from player in Gen.Choose(1, Players)
        from container in Gen.Choose(0, 2)
        from slot in Gen.Choose(0, 39)
        from type in Gen.Choose(0, _types.Length - 1)
        from count in Gen.Choose(1, 6)
        from penalty in Gen.Elements(DeathPenalty.Mild, DeathPenalty.Normal, DeathPenalty.Normal, DeathPenalty.Hard, DeathPenalty.Hard)
        from pick in Gen.Choose(0, 5)
        from free in Gen.Choose(0, 3)
        select new Step(action, player, container, slot, type, count, penalty, pick, free);

    private static Gen<Step[]> Sequence { get; } = StepGen.ArrayOf(OperationsPerSequence);

    [Fact]
    public void RandomDeathsAndRecovery_ConserveEveryItem()
    {
        Kit.ForAll(Sequence, sequence => Run(sequence, null), Sequences);

        var coverage = new Coverage();
        foreach (var sequence in Sequence.Sample(CoverageSequences, 20))
        {
            Run(sequence, coverage);
        }

        // Measured over 300 sequences of 40 steps (see the run log of the package); the thresholds are the minimums of the plan (K6-35, tests of stage A): 50 deaths under each penalty,
        // 20 partial take-alls, 20 last recoveries, 10 hard deaths with equipment.
        Assert.True(coverage.Mild >= 50, $"{coverage.Mild} mild deaths");
        Assert.True(coverage.Normal >= 50, $"{coverage.Normal} normal deaths");
        Assert.True(coverage.Hard >= 50, $"{coverage.Hard} hard deaths");
        Assert.True(coverage.PartialTakeAll >= 20, $"{coverage.PartialTakeAll} partial take-alls");
        Assert.True(coverage.LastRecoveries >= 20, $"{coverage.LastRecoveries} last recoveries");
        Assert.True(coverage.HardWithEquipment >= 10, $"{coverage.HardWithEquipment} hard deaths with equipment");
        Assert.True(coverage.Refused >= 10, $"{coverage.Refused} refused intents");
    }

    private static void Run(Step[] steps, Coverage? coverage)
    {
        var w = new DeathWorld(Players, survival: true);
        var expected = new long[w.Totals().Length];
        foreach (var step in steps)
        {
            var player = new PlayerId((ulong)step.Player);

            // The buckets of the intents refill with the ticks.
            w.Hazard.Blocks.Sim.Ticks(10);
            switch (step.Action)
            {
                case Action.Give:
                    Give(w, player, step, expected);
                    break;
                case Action.Die:
                    Die(w, player, step, coverage);
                    break;
                case Action.Crowd:
                    Crowd(w, player, step, expected);
                    break;
                default:
                    Recover(w, player, step, coverage);
                    break;
            }

            Assert.Equal(expected, w.Totals());
            AssertTombstones(w);
        }
    }

    private static Container? Slotted(DeathWorld w, PlayerId player, int container)
    {
        var kind = container switch { 0 => ContainerKind.PlayerBag, 1 => ContainerKind.Hotbar, _ => ContainerKind.Equipment };
        return w.State.Containers.TryGetOfPlayer(player, kind, out var found) ? found : null;
    }

    private static void Give(DeathWorld w, PlayerId player, Step step, long[] expected)
    {
        var container = Slotted(w, player, step.Container)!;
        var slot = step.Slot % container.Size;
        var type = _types[step.Type];
        var current = container.GetSlot(slot);
        if (!current.IsEmpty && current.Type != type)
        {
            return;
        }

        w.Put(container.Id, slot, type, step.Count);
        expected[type.Value] += step.Count;
    }

    private static void Crowd(DeathWorld w, PlayerId player, Step step, long[] expected)
    {
        foreach (var container in new[] { Slotted(w, player, 0)!, Slotted(w, player, 1)! })
        {
            for (var slot = 0; slot < container.Size - step.Free; slot++)
            {
                if (container.GetSlot(slot).IsEmpty)
                {
                    w.Put(container.Id, slot, DeathWorld.Tool, 1);
                    expected[DeathWorld.Tool.Value]++;
                }
            }
        }
    }

    private static void Die(DeathWorld w, PlayerId player, Step step, Coverage? coverage)
    {
        var equipped = Slotted(w, player, 2)!.Slots.ToArray().Any(s => !s.IsEmpty);
        w.Die(player, step.Penalty);
        if (coverage is null)
        {
            return;
        }

        coverage.Mild += step.Penalty == DeathPenalty.Mild ? 1 : 0;
        coverage.Normal += step.Penalty == DeathPenalty.Normal ? 1 : 0;
        coverage.Hard += step.Penalty == DeathPenalty.Hard ? 1 : 0;
        coverage.HardWithEquipment += step.Penalty == DeathPenalty.Hard && equipped ? 1 : 0;
    }

    private static void Recover(DeathWorld w, PlayerId player, Step step, Coverage? coverage)
    {
        var owned = w.State.PersistentEntities.OwnedBy(player);
        if (owned.Count == 0)
        {
            return;
        }

        var entity = owned[step.Pick % owned.Count];
        Assert.True(w.State.Containers.TryGetOfEntity(entity.Id, out var tombstone));
        var before = Stacks(tombstone);
        var hash = StateHasher.Hash(w.State);
        var bag = Slotted(w, player, 0)!;

        Intent intent = new TakeAllIntent(tombstone.Id);
        if (step.Action == Action.MoveOne)
        {
            var slot = 0;
            while (slot < tombstone.Size && tombstone.GetSlot(slot).IsEmpty)
            {
                slot++;
            }

            var stack = tombstone.GetSlot(slot);
            intent = new ItemMoveIntent(new SlotRef(tombstone.Id, (ushort)slot), ItemMoveTarget.AnyIn(bag.Id), stack.Count, stack.Type, stack.Count);
        }

        var result = w.Hazard.Blocks.SubmitAs(player, intent);
        if (!result.IsAccepted)
        {
            Assert.Equal(hash, StateHasher.Hash(w.State));
            Assert.True(w.State.Containers.TryGet(tombstone.Id, out _));
            coverage?.Add(c => c.Refused++);
            return;
        }

        if (!w.State.Containers.TryGet(tombstone.Id, out var after))
        {
            Assert.False(w.State.PersistentEntities.TryGet(entity.Id, out _));
            coverage?.Add(c => c.LastRecoveries++);
        }
        else if (step.Action == Action.TakeAll && Stacks(after) < before && Stacks(after) > 0)
        {
            coverage?.Add(c => c.PartialTakeAll++);
        }
    }

    private static int Stacks(Container container)
    {
        var count = 0;
        foreach (var stack in container.Slots)
        {
            count += stack.IsEmpty ? 0 : 1;
        }

        return count;
    }

    /// <summary>Every tombstone is private, owned, hosted by an entity of the same owner in a planet frame, and holds something; every entity hosts a container.</summary>
    private static void AssertTombstones(DeathWorld w)
    {
        var tombstones = w.Tombstones();
        Assert.Equal(w.State.PersistentEntities.Count, tombstones.Count);
        foreach (var container in tombstones)
        {
            Assert.Equal(ContainerAccess.Private, container.Access);
            Assert.True(w.State.PersistentEntities.TryGet(new EntityId(container.Host.Id), out var entity));
            Assert.Equal(entity.Owner, container.Owner);
            Assert.Equal(FrameKind.Planet, entity.Frame.Kind);
            Assert.True(Stacks(container) > 0, "an empty tombstone");
        }
    }

    private sealed record Step(Action Action, int Player, int Container, int Slot, int Type, int Count, DeathPenalty Penalty, int Pick, int Free);

    private sealed class Coverage
    {
        public int Mild { get; set; }

        public int Normal { get; set; }

        public int Hard { get; set; }

        public int HardWithEquipment { get; set; }

        public int PartialTakeAll { get; set; }

        public int LastRecoveries { get; set; }

        public int Refused { get; set; }

        public void Add(System.Action<Coverage> change) => change(this);
    }
}
