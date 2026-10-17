using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// K6-35, D1, D5, D6 and D8: the death is one compound. The reparent of the player is its only irreversible component and the last one, checked before the others;
/// a failing component leaves no trace; a replay does not depend on the health of the moment.
/// </summary>
public sealed class DeathCompoundTests
{
    private static readonly PlayerId _p1 = DeathWorld.Player1;

    private static DeathWorld Armed()
    {
        var w = new DeathWorld();
        w.GiveKit(_p1);
        return w;
    }

    private static EntityReparentOp Reparent(DeathWorld w, bool reset = true, uint seqOffset = 1) =>
        new(
            w.Hazard.Blocks.Sim.PlayerOf(_p1).Entity, FrameRef.Of(WorldFactory.HomePlanet), w.Hazard.Blocks.Sim.PlayerOf(_p1).ParentSeq + seqOffset,
            new Vector3(3f, 4000.2f, 1f), Quaternion.Identity, Vector3.Zero, null, reset);

    /// <summary>The tombstone entity and its container, as the builder makes them.</summary>
    private static Operation[] TombstoneParts(DeathWorld w, ContainerAccess access = ContainerAccess.Private) =>
    [
        new PersistentEntityCreateOp(
            w.State.EntityIds.PeekNext(), PersistentEntityKind.Tombstone, FrameRef.Of(WorldFactory.HomePlanet), 0, new Vector3(1f, 4000f, 0f), Quaternion.Identity, _p1),
        new ContainerCreateOp(
            w.State.ContainerIds.PeekNext(), ContainerKind.Tombstone, ContainerHost.PersistentEntity(w.State.EntityIds.PeekNext()), _p1, access, 2),
    ];

    [Fact]
    public void Apply_FailingComponent_RollsBackEverything()
    {
        var w = Armed();
        var hash = w.Hash();
        var last = w.State.LastOpId;
        var entityIds = w.State.EntityIds.NextValue;
        var containerIds = w.State.ContainerIds.NextValue;
        var containers = w.State.Containers.All.ToDictionary(c => c.Id, c => c.Version);
        var player = w.Hazard.Blocks.Sim.PlayerOf(_p1);
        var (frame, seq, position, vitals) = (player.Frame, player.ParentSeq, player.Position, player.Vitals);
        w.State.CompleteCheckpoint(w.State.CaptureCheckpoint());
        var journaled = w.Hazard.Blocks.Sim.Journal.Entries.Count;

        // The entity, the container and the first transfer are fine; the second transfer finds another type; the reparent would be fine.
        var parts = TombstoneParts(w);
        var bag = DeathWorld.BagOf(_p1);
        var op = new CompoundOp(
        [
            .. parts,
            new ItemTransferOp(new SlotRef(bag, 0), new SlotRef(w.State.ContainerIds.PeekNext(), 0), DeathWorld.Stone, 30, 500),
            new ItemTransferOp(new SlotRef(bag, 5), new SlotRef(w.State.ContainerIds.PeekNext(), 1), DeathWorld.Stone, 3, 500),
            new FoodSetOp(_p1, default),
            Reparent(w),
        ]);

        w.Hazard.AssertFails(op);
        Assert.False(w.TryApplyRaw(op, out _, out var failure, out var index));
        Assert.Equal(3, index);
        Assert.NotEmpty(failure);

        Assert.Equal(hash, w.Hash());
        Assert.Equal(last, w.State.LastOpId);
        Assert.Equal(entityIds, w.State.EntityIds.NextValue);
        Assert.Equal(containerIds, w.State.ContainerIds.NextValue);
        Assert.Equal(0, w.State.PersistentEntities.Count);
        Assert.Equal(containers, w.State.Containers.All.ToDictionary(c => c.Id, c => c.Version));
        Assert.Equal(new ItemStack(DeathWorld.Stone, 30), w.Container(bag).GetSlot(0));
        Assert.Equal(new ItemStack(DeathWorld.Gem, 3), w.Container(bag).GetSlot(5));
        Assert.True(w.State.Progress.TryGet(_p1, out var progress) && !progress.Food.IsEmpty);
        Assert.Equal((frame, seq, position, vitals), (player.Frame, player.ParentSeq, player.Position, player.Vitals));
        Assert.Equal(journaled, w.Hazard.Blocks.Sim.Journal.Entries.Count);

        // The checkpoint marks of the failed attempt are gone as well.
        var checkpoint = w.State.CaptureCheckpoint();
        Assert.Empty(checkpoint.UpsertedContainers);
        Assert.Empty(checkpoint.RemovedContainers);
        Assert.Empty(checkpoint.UpsertedPersistentEntities);
        Assert.Empty(checkpoint.RemovedPersistentEntities);
        Assert.Empty(checkpoint.UpsertedProgress);
    }

    [Fact]
    public void Apply_Death_IsOneJournalEntryAndTheSameWorldAsTheParts()
    {
        var w = Armed();
        var journaled = w.Hazard.Blocks.Sim.Journal.Entries.Count;

        var applied = w.Die(_p1, Horyzont.Core.World.DeathPenalty.Normal);

        Assert.Equal(journaled + 1, w.Hazard.Blocks.Sim.Journal.Entries.Count);
        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.IsType<EntityReparentOp>(compound.Components[^1]);
        Assert.True(((EntityReparentOp)compound.Components[^1]).ResetVitals);
        Assert.Equal(1, compound.Components.Count(c => c is PersistentEntityCreateOp));
        Assert.Equal(1, compound.Components.Count(c => c is ContainerCreateOp));
        Assert.Equal(4, compound.Components.Count(c => c is ItemTransferOp));
        Assert.Equal(1, compound.Components.Count(c => c is FoodSetOp));
        Assert.Contains(applied.ContainerChanges, change => !change.Removed);
    }

    [Fact]
    public void Reparent_NotLast_RejectedStructurally()
    {
        var w = Armed();
        var feed = new FoodSetOp(_p1, default);

        // The reparent of a player in front of another component.
        AssertStructure(w, new CompoundOp([Reparent(w), feed]), 0);

        // The reparent of a construct in a compound, last or not; and a reset for anything but a player.
        var construct = new EntityReparentOp(
            Horyzont.Testing.FunctionalBlocks.Construct, FrameRef.Of(WorldFactory.HomePlanet), 1, new Vector3(0f, 4000f, 0f), Quaternion.Identity, Vector3.Zero, null);
        AssertStructure(w, new CompoundOp([feed, construct]), 1);
        w.Hazard.AssertFails(construct with { ResetVitals = true });

        // Two reparents: there is room for one irreversible component only.
        AssertStructure(w, new CompoundOp([Reparent(w), Reparent(w)]), 0);
    }

    private static void AssertStructure(DeathWorld w, CompoundOp op, int componentIndex)
    {
        var hash = w.Hash();
        Assert.False(w.TryApplyRaw(op, out _, out var failure, out var index));
        Assert.Equal(componentIndex, index);
        Assert.NotEmpty(failure);
        Assert.Equal(hash, w.Hash());
    }

    [Fact]
    public void Reparent_FailingPrecondition_ChecksBeforeOtherComponents()
    {
        var w = Armed();
        var bag = DeathWorld.BagOf(_p1);

        // The first component would fail by itself (nothing in the slot); the reparent has a wrong ParentSeq. The reparent is judged first, so it is the one reported,
        // and the earlier components were not even tried.
        var op = new CompoundOp([new ItemDestroyOp(new SlotRef(bag, 20), DeathWorld.Stone, 1, ItemSink.Admin), Reparent(w, seqOffset: 5)]);

        Assert.False(w.TryApplyRaw(op, out _, out var failure, out var index));
        Assert.Equal(1, index);
        Assert.Contains("ParentSeq", failure);

        // With a correct reparent the earlier component is the one that fails.
        Assert.False(w.TryApplyRaw(new CompoundOp([new ItemDestroyOp(new SlotRef(bag, 20), DeathWorld.Stone, 1, ItemSink.Admin), Reparent(w)]), out _, out _, out var first));
        Assert.Equal(0, first);
    }

    [Fact]
    public void Reparent_ResetVitals_ClearsTheVitalsOnlyWhenApplied()
    {
        var w = Armed();
        var player = w.Hazard.Blocks.Sim.PlayerOf(_p1);
        Assert.NotNull(player.Vitals);

        // A refused reset leaves the vitals alone (a wrong ParentSeq).
        w.Hazard.AssertFails(Reparent(w, seqOffset: 2));
        Assert.NotNull(player.Vitals);

        // A plain reparent (format 1, no reset) leaves them too: boarding does not heal.
        w.Apply(Reparent(w, reset: false));
        Assert.NotNull(player.Vitals);

        w.Apply(Reparent(w));
        Assert.Null(player.Vitals);
    }

    [Fact]
    public void Replay_CheckpointHadPositiveHealth_AppliesReset()
    {
        // D8: the applier never reads the health. A replay on a world loaded from a checkpoint in which the player was still healthy ends in the same world as the live one.
        var live = Armed();
        live.Die(_p1, Horyzont.Core.World.DeathPenalty.Hard);
        var entries = live.Hazard.Blocks.Sim.Journal.Entries.ToArray();

        var replayed = new DeathWorld();
        replayed.GiveKit(_p1);
        Assert.NotNull(replayed.Hazard.VitalsOf(_p1));
        var simulation = new ServerSimulation(
            replayed.State, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        var deathEntry = entries[^1];
        Assert.IsType<CompoundOp>(deathEntry.Operation);

        // The setup of the live world is the same as the setup of the replayed one, so the last journal entry is the death.
        simulation.Replay(deathEntry);

        Assert.Null(replayed.Hazard.VitalsOf(_p1));
        Assert.Equal(live.Hash(), replayed.Hash());
        Assert.Single(replayed.Tombstones());
    }
}
