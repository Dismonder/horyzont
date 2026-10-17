using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.Survival;
using Horyzont.Core.World;

namespace Horyzont.Core.Tests.Survival;

/// <summary>K6-35, D3 and D5 to D11: what each penalty moves, what a death always does and that no item is lost.</summary>
public sealed class DeathRulesTests
{
    private static readonly PlayerId _p1 = DeathWorld.Player1;

    private static DeathWorld Armed()
    {
        var w = new DeathWorld();
        w.GiveKit(_p1);
        return w;
    }

    [Fact]
    public void Death_Mild_KeepsAllItems()
    {
        var w = Armed();
        var before = w.Held(_p1);
        var totals = w.Totals();
        Assert.Equal(6, before.Count);

        w.Die(_p1, DeathPenalty.Mild);

        Assert.Equal(before, w.Held(_p1));
        Assert.Equal(totals, w.Totals());
        Assert.Empty(w.Tombstones());
        Assert.Equal(0, w.State.PersistentEntities.Count);
    }

    [Fact]
    public void Death_Normal_MovesBagAndHotbar()
    {
        var w = Armed();
        var totals = w.Totals();

        w.Die(_p1, DeathPenalty.Normal);

        Assert.All(w.Held(_p1), held => Assert.Equal(ContainerKind.Equipment, held.Kind));
        Assert.Equal(2, w.Held(_p1).Count);
        var tombstone = Assert.Single(w.Tombstones());
        Assert.Equal(4, Count(tombstone));
        Assert.Equal(totals, w.Totals());
        AssertTombstone(w, tombstone);
    }

    [Fact]
    public void Death_Hard_MovesAllEquipment()
    {
        var w = Armed();
        var totals = w.Totals();

        w.Die(_p1, DeathPenalty.Hard);

        Assert.Empty(w.Held(_p1));
        var tombstone = Assert.Single(w.Tombstones());
        Assert.Equal(6, Count(tombstone));
        Assert.Equal(totals, w.Totals());
        AssertTombstone(w, tombstone);
    }

    [Theory]
    [InlineData(DeathPenalty.Mild)]
    [InlineData(DeathPenalty.Normal)]
    [InlineData(DeathPenalty.Hard)]
    public void Death_AllPenalties_ClearsFoodAndResetsVitals(DeathPenalty penalty)
    {
        var w = Armed();
        Assert.False(w.State.Progress.TryGet(_p1, out var armed) && armed.Food.IsEmpty);

        w.Die(_p1, penalty);

        Assert.True(w.State.Progress.TryGet(_p1, out var progress));
        Assert.True(progress.Food.IsEmpty);
        Assert.Null(w.Hazard.VitalsOf(_p1));

        // One step of the vitals service on a fresh body: the maxima of the base, no rest, no exhaustion, a fresh suit.
        var next = w.NextStepVitals(_p1);
        Assert.Equal(100f, next.Health);
        Assert.Equal(100f, next.Stamina);
        Assert.Equal(default, next.RestedUntil);
        Assert.False(next.Exhausted);
        Assert.Null(next.Suit);
    }

    [Fact]
    public void Death_PreservesDiscoveriesAndBed()
    {
        var w = Armed();
        w.State.Progress.TryGet(_p1, out var before);
        Assert.NotNull(before!.RespawnBed);

        w.Die(_p1, DeathPenalty.Hard);

        Assert.True(w.State.Progress.TryGet(_p1, out var after));
        Assert.Equal(before.RespawnBed, after.RespawnBed);
        Assert.True(after.DiscoveredItems.Contains(DeathWorld.Stone));
        Assert.Equal(before.Discovered, after.Discovered);
    }

    [Fact]
    public void Death_WithoutItemsOrFood_OnlyMovesTheBody()
    {
        var w = new DeathWorld();
        var hash = w.Hash();

        var applied = w.Die(_p1, DeathPenalty.Hard);

        Assert.IsType<EntityReparentOp>(applied.Operation);
        Assert.Empty(w.Tombstones());
        Assert.Equal(0, w.State.PersistentEntities.Count);
        Assert.NotEqual(hash, w.Hash());
    }

    [Fact]
    public void Death_MovesTheBodyToTheRespawnWithANewParentSeq()
    {
        var w = Armed();
        var player = w.Hazard.Blocks.Sim.PlayerOf(_p1);
        var seq = player.ParentSeq;
        var respawn = new RespawnPoint(RespawnSource.WorldStart, new RespawnPose(Horyzont.Testing.WorldFactory.HomePlanet, new System.Numerics.Vector3(3f, 4000.2f, 1f), System.Numerics.Quaternion.Identity));

        w.Die(_p1, DeathPenalty.Normal, respawn: respawn);

        Assert.Equal(seq + 1, player.ParentSeq);
        Assert.Equal(respawn.Pose.Position, player.Position);
        Assert.Equal(FrameRef.Of(respawn.Pose.Planet), player.Frame);
        Assert.Null(player.Seat);
    }

    [Fact]
    public void Death_Over256Stacks_SplitsWithoutLoss()
    {
        var w = new DeathWorld();
        w.Apply(new ContainerConfigureOp(DeathWorld.BagOf(_p1), 256, ContainerAccess.Private));
        for (var i = 0; i < 256; i++)
        {
            w.Put(DeathWorld.BagOf(_p1), i, DeathWorld.Stone, 1 + (i % 7));
        }

        for (var i = 0; i < 10; i++)
        {
            w.Put(DeathWorld.HotbarOf(_p1), i, DeathWorld.Gem, 2);
        }

        var totals = w.Totals();

        w.Die(_p1, DeathPenalty.Normal);

        var tombstones = w.Tombstones();
        Assert.Equal(2, tombstones.Count);
        Assert.Equal(256, tombstones[0].Size);
        Assert.Equal(10, tombstones[1].Size);
        Assert.Equal(256, Count(tombstones[0]));
        Assert.Equal(10, Count(tombstones[1]));
        Assert.Equal(2, w.State.PersistentEntities.Count);
        Assert.Equal(totals, w.Totals());
        Assert.Empty(w.Held(_p1));
        foreach (var tombstone in tombstones)
        {
            AssertTombstone(w, tombstone);
        }
    }

    [Fact]
    public void Death_StackAboveCurrentLimit_PreservesCount()
    {
        var w = new DeathWorld();
        w.Put(DeathWorld.BagOf(_p1), 0, DeathWorld.Gem, 500);
        var plan = DeathInventoryPlan.Create(w.State, _p1, DeathPenalty.Normal);
        Assert.Equal(1, plan.StackCount);

        // The content says a gem stacks to 5: the transfer still carries 500 as its limit, so the applier does not refuse the old, larger stack.
        var op = DeathOperationBuilder.Build(w.State, _p1, DeathPenalty.Normal, w.DefaultPlacement(_p1), w.StayHere(_p1), _ => 5);
        var compound = Assert.IsType<CompoundOp>(op);
        var transfer = Assert.Single(compound.Components.OfType<ItemTransferOp>());
        Assert.Equal(500, transfer.Count);
        Assert.Equal(500, transfer.ToStackLimit);

        w.Apply(op);

        var tombstone = Assert.Single(w.Tombstones());
        Assert.Equal(new ItemStack(DeathWorld.Gem, 500), tombstone.GetSlot(0));
        Assert.Empty(w.Held(_p1));
    }

    [Fact]
    public void Plan_TakesStacksInTheOrderBagHotbarEquipmentAndNeverMerges()
    {
        var w = Armed();
        w.Put(DeathWorld.BagOf(_p1), 9, DeathWorld.Gem, 2);

        var plan = DeathInventoryPlan.Create(w.State, _p1, DeathPenalty.Hard);

        var tombstone = Assert.Single(plan.Tombstones);
        Assert.Equal(7, plan.StackCount);
        Assert.Equal(7, tombstone.Size);
        Assert.Equal(
            [DeathWorld.BagOf(_p1), DeathWorld.BagOf(_p1), DeathWorld.BagOf(_p1), DeathWorld.HotbarOf(_p1), DeathWorld.HotbarOf(_p1), DeathWorld.EquipmentOf(_p1), DeathWorld.EquipmentOf(_p1)],
            tombstone.Transfers.Select(t => t.From.Container).ToArray());
        Assert.Equal(new ushort[] { 0, 5, 9, 0, 1, 0, 1 }, tombstone.Transfers.Select(t => t.From.Slot).ToArray());
        Assert.Equal(new ushort[] { 0, 1, 2, 3, 4, 5, 6 }, tombstone.Transfers.Select(t => t.TargetSlot).ToArray());
    }

    [Fact]
    public void Plan_MildOrEmpty_HasNoTombstone()
    {
        var w = Armed();
        Assert.Empty(DeathInventoryPlan.Create(w.State, _p1, DeathPenalty.Mild).Tombstones);
        Assert.Empty(DeathInventoryPlan.Create(new DeathWorld().State, _p1, DeathPenalty.Hard).Tombstones);
    }

    [Fact]
    public void Rules_MovedKindsAndDeadVitals()
    {
        Assert.Empty(DeathRules.MovedKinds(DeathPenalty.Mild));
        Assert.Equal([ContainerKind.PlayerBag, ContainerKind.Hotbar], DeathRules.MovedKinds(DeathPenalty.Normal));
        Assert.Equal([ContainerKind.PlayerBag, ContainerKind.Hotbar, ContainerKind.Equipment], DeathRules.MovedKinds(DeathPenalty.Hard));

        Assert.False(DeathRules.IsDead(null));
        Assert.False(DeathRules.IsDead(new PlayerVitals(1f, 0f, default, false)));
        Assert.True(DeathRules.IsDead(new PlayerVitals(0f, 50f, default, false)));
    }

    private static int Count(Container container)
    {
        var stacks = 0;
        foreach (var stack in container.Slots)
        {
            stacks += stack.IsEmpty ? 0 : 1;
        }

        return stacks;
    }

    /// <summary>A tombstone container is private, owned by the player, hosted by a tombstone entity of the same owner in the frame of a planet.</summary>
    private static void AssertTombstone(DeathWorld w, Container container)
    {
        Assert.Equal(ContainerAccess.Private, container.Access);
        Assert.Equal(_p1, container.Owner);
        Assert.Equal(ContainerHostKind.PersistentEntity, container.Host.Kind);
        Assert.True(w.State.PersistentEntities.TryGet(new Horyzont.Core.Ids.EntityId(container.Host.Id), out var entity));
        Assert.Equal(PersistentEntityKind.Tombstone, entity.Kind);
        Assert.Equal(_p1, entity.Owner);
        Assert.Equal(FrameKind.Planet, entity.Frame.Kind);
    }
}
