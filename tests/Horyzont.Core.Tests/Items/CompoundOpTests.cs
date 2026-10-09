using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;

using static Horyzont.Core.Tests.Items.ItemFixture;

namespace Horyzont.Core.Tests.Items;

public sealed class CompoundOpTests
{
    [Fact]
    public void FailingLastComponent_RollsBackAll()
    {
        var f = new ItemFixture();
        var player = new PlayerId(1);
        var hash = StateHasher.Hash(f.State);
        var nextContainer = f.State.ContainerIds.NextValue;
        var versions = f.State.Containers.All.Select(c => c.Version).ToArray();
        var last = f.State.LastOpId;
        var compound = new CompoundOp(
        [
            Create(BagSlot(0), Stone, 5),
            Transfer(BagSlot(0), BagSlot(1), Stone, 2),
            new ContainerCreateOp(new ContainerId(nextContainer), ContainerKind.Chest, ContainerHost.Player(player), player, ContainerAccess.Private, 4),
            new ItemSwapOp(BagSlot(1), HotbarSlot(0), Stone, ItemTypeId.None),
            Destroy(BagSlot(0), Stone, 10),
        ]);

        var ok = TryApply(f, compound, out _, out var failure, out var index);

        Assert.False(ok);
        Assert.Equal(4, index);
        Assert.NotEmpty(failure);
        Assert.Equal(hash, StateHasher.Hash(f.State));
        Assert.Equal(nextContainer, f.State.ContainerIds.NextValue);
        Assert.Equal(versions, f.State.Containers.All.Select(c => c.Version));
        Assert.Equal(last, f.State.LastOpId);
        f.AssertFails(compound);
    }

    [Fact]
    public void TerrainNotLast_Fails()
    {
        var f = new ItemFixture();
        var compound = new CompoundOp([Dig(f), Create(BagSlot(0), Stone, 2, source: ItemSource.Harvest)]);

        Assert.False(TryApply(f, compound, out _, out _, out var index));

        Assert.Equal(0, index);
        f.AssertFails(compound);
        Assert.Empty(f.Sim.Terrain.AppliedEdits);
    }

    [Fact]
    public void TwoTerrainEdits_Fails()
    {
        var f = new ItemFixture();
        var compound = new CompoundOp([Create(BagSlot(0), Stone, 2), Dig(f), Dig(f)]);

        Assert.False(TryApply(f, compound, out _, out _, out var index));

        Assert.Equal(1, index);
        f.AssertFails(compound);
        Assert.Empty(f.Sim.Terrain.AppliedEdits);
    }

    [Fact]
    public void Nested_Fails()
    {
        var f = new ItemFixture();
        var compound = new CompoundOp([Create(BagSlot(0), Stone, 2), new CompoundOp([Create(BagSlot(1), Stone, 2)])]);

        Assert.False(TryApply(f, compound, out _, out _, out var index));

        Assert.Equal(1, index);
        f.AssertFails(compound);
    }

    [Fact]
    public void EmptyOrDisallowedComponent_Fails()
    {
        var f = new ItemFixture();
        var player = f.Sim.PlayerOf(new PlayerId(1));

        f.AssertFails(new CompoundOp([]));
        f.AssertFails(new CompoundOp(
        [
            Create(BagSlot(0), Stone, 2),
            new PlayerLeaveOp(player.Player, player.Frame, player.ParentSeq, player.Position, Quaternion.Identity, 0f),
        ]));
    }

    [Fact]
    public void VersionBumpsOncePerContainer()
    {
        var f = new ItemFixture();
        var bag = f.Container(Bag).Version;
        var hotbar = f.Container(Hotbar).Version;

        var applied = f.Apply(new CompoundOp(
        [
            Create(BagSlot(0), Stone, 5),
            Create(BagSlot(1), Soil, 5),
            Transfer(BagSlot(0), HotbarSlot(0), Stone, 5),
            Transfer(BagSlot(1), BagSlot(0), Soil, 1),
        ]));

        Assert.Equal(bag + 1, f.Container(Bag).Version);
        Assert.Equal(hotbar + 1, f.Container(Hotbar).Version);
        Assert.Equal(2, applied.ContainerChanges.Count);
        Assert.Equal(new ContainerChange(Bag, bag + 1, applied.ContainerChanges[0].Slots), applied.ContainerChanges[0]);
        Assert.Equal([(ushort)0, (ushort)1], applied.ContainerChanges[0].Slots);
        Assert.Equal(Hotbar, applied.ContainerChanges[1].Container);
        Assert.Equal([(ushort)0], applied.ContainerChanges[1].Slots);
        Assert.Equal(new ItemStack(Soil, 1), f.Slot(BagSlot(0)));
    }

    [Fact]
    public void TerrainAppliedOnlyAfterItemComponentsHold()
    {
        var f = new ItemFixture();
        var failing = new CompoundOp([Destroy(BagSlot(0), Soil, 1, ItemSink.BuildCost), Dig(f)]);

        Assert.False(TryApply(f, failing, out _, out _, out var index));
        Assert.Equal(0, index);
        f.AssertFails(failing);
        Assert.Empty(f.Sim.Terrain.AppliedEdits);

        var applied = f.Apply(new CompoundOp([Create(BagSlot(0), Stone, 6, source: ItemSource.Harvest), Dig(f)]));

        Assert.Single(f.Sim.Terrain.AppliedEdits);
        Assert.NotEmpty(applied.TerrainVersions);
        Assert.Equal(new ItemStack(Stone, 6), f.Slot(BagSlot(0)));
        Assert.True(OperationTerrain.TryGetTerrainEdit(applied.Operation, out var edit));
        Assert.Equal(f.Sim.Terrain.AppliedEdits[0], edit);
    }

    [Fact]
    public void TerrainComponentPreconditionFails_ItemsRolledBack()
    {
        var f = new ItemFixture();
        var badEdit = Dig(f) with { Radius = 1000f };
        var compound = new CompoundOp([Create(BagSlot(0), Stone, 6, source: ItemSource.Harvest), badEdit]);

        Assert.False(TryApply(f, compound, out _, out _, out var index));

        Assert.Equal(1, index);
        f.AssertFails(compound);
        Assert.Empty(f.Sim.Terrain.AppliedEdits);
    }

    [Fact]
    public void ContainerCreateThenCreateItems_InOneCompound()
    {
        var f = new ItemFixture();
        var second = f.Sim.Join(2, "second");
        var bag = f.State.ContainerIds.PeekNext();
        var hotbar = new ContainerId(bag.Value + 1);

        var applied = f.Apply(new CompoundOp(
        [
            new ContainerCreateOp(bag, ContainerKind.PlayerBag, ContainerHost.Player(second), second, ContainerAccess.Private, 40),
            new ContainerCreateOp(hotbar, ContainerKind.Hotbar, ContainerHost.Player(second), second, ContainerAccess.Private, 10),
            Create(new SlotRef(hotbar, 0), Tool, 1, limit: 1, source: ItemSource.Starter),
            Create(new SlotRef(hotbar, 1), Gem, 3, limit: 5, source: ItemSource.Starter),
        ]));

        Assert.Equal(1u, f.Container(bag).Version);
        Assert.Equal(1u, f.Container(hotbar).Version);
        Assert.Equal(new ItemStack(Gem, 3), f.Slot(new SlotRef(hotbar, 1)));
        Assert.True(f.State.Containers.TryGetOfPlayer(second, ContainerKind.Hotbar, out var found));
        Assert.Equal(hotbar, found.Id);
        Assert.Equal([bag, hotbar], applied.ContainerChanges.Select(c => c.Container));
        Assert.Equal(40, applied.ContainerChanges[0].Slots.Count);
        Assert.Equal(10, applied.ContainerChanges[1].Slots.Count);
    }

    [Fact]
    public void OperationTerrain_FindsTopLevelAndLastComponentOnly()
    {
        var f = new ItemFixture();
        var dig = Dig(f);

        Assert.True(OperationTerrain.TryGetTerrainEdit(dig, out var top));
        Assert.Equal(dig, top);
        Assert.True(OperationTerrain.TryGetTerrainEdit(new CompoundOp([Create(BagSlot(0), Stone, 1), dig]), out var last));
        Assert.Equal(dig, last);
        Assert.False(OperationTerrain.TryGetTerrainEdit(new CompoundOp([Create(BagSlot(0), Stone, 1)]), out _));
        Assert.False(OperationTerrain.TryGetTerrainEdit(Create(BagSlot(0), Stone, 1), out _));
    }

    internal static TerrainEditOp Dig(ItemFixture f) =>
        new(SimFixture.Planet, TerrainShape.Sphere, f.Sim.Ground(3), 0.5f, TerrainEditMode.Remove, 0);

    private static bool TryApply(ItemFixture f, Operation op, out AppliedOperation? applied, out string failure, out int index) =>
        new OperationApplier(f.State, f.Sim.Terrain).TryApply(f.State.LastOpId.Next, f.State.CurrentTick, null, op, out applied, out failure, out index);
}
