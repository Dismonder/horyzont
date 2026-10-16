using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Items;

/// <summary>K6-30, rules of the compound operation with functional blocks (D12) and the new conditions of operations 4 and 35 (D13).</summary>
public sealed class CompoundConstructTests
{
    private static readonly CellPos _chestCell = FunctionalBlocks.ChestCell;

    [Fact]
    public void PlaceChestOnTerrain_Compound_CreatesConstructContainerAndEntity()
    {
        var f = new FunctionalBlockFixture(withConstruct: false);

        var (applied, construct, container) = PlaceChestOnTerrain(f);

        Assert.True(f.State.Constructs.TryGet(construct, out var created));
        Assert.Equal(1u, created.Version);
        Assert.Equal(FunctionalBlocks.ChestType, created.GetBlock(default).TypeId);
        Assert.Equal(ContainerHost.ConstructBlock(construct, default), f.Container(container).Host);
        Assert.Equal(ContainerKind.Chest, f.Container(container).Kind);
        Assert.True(f.State.BlockEntities.TryGet(new BlockRef(construct, default), out var state));
        Assert.Equal(new ChestBlockState(container), state);
        Assert.Equal(new ConstructChange(construct, 1, false), Assert.Single(applied.ConstructChanges));
        Assert.Equal(container, Assert.Single(applied.ContainerChanges, c => c.Container == container).Container);
    }

    [Fact]
    public void PlaceChestOnFace_Compound_ConstructVersionPlusOneOnce()
    {
        var f = new FunctionalBlockFixture();
        var version = f.ConstructVersion;
        var container = f.State.ContainerIds.PeekNext();

        var applied = f.Apply(PlaceChestCompound(container, FunctionalBlocks.FreeCell));

        Assert.Equal(version + 1, f.ConstructVersion);
        Assert.Equal(new ConstructChange(ConstructId, version + 1, false), Assert.Single(applied.ConstructChanges));
        Assert.Equal(FunctionalBlocks.ChestType, f.BlockAt(FunctionalBlocks.FreeCell).TypeId);
        Assert.True(f.HasState(FunctionalBlocks.FreeCell, out var state));
        Assert.Equal(new ChestBlockState(container), state);
        Assert.Equal(1u, f.Container(container).Version);
    }

    [Fact]
    public void ConstructComponentNotLast_Fails()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(PlaceChestCompound(f.State.ContainerIds.PeekNext(), FunctionalBlocks.FreeCell));
        var next = f.State.ContainerIds.PeekNext();

        // The block is placed first and the container created afterwards: the construct component has to come last.
        var blockFirst = new CompoundOp([PlaceBlock(FunctionalBlocks.FreeCell2, FunctionalBlocks.ChestType), ChestContainer(next, ConstructId, _chestCell)]);
        var clearFirst = new CompoundOp([new BlockClearOp(ConstructId, FunctionalBlocks.LampCell), ChestContainer(next, ConstructId, _chestCell)]);

        Assert.False(f.TryApplyRaw(blockFirst, out _, out _, out _));
        f.AssertFails(blockFirst);
        f.AssertFails(clearFirst);
        Assert.Equal(BlockState.Empty, f.BlockAt(FunctionalBlocks.FreeCell2));
        Assert.Empty(f.HostedBy(_chestCell));
    }

    [Fact]
    public void TerrainAndBlockInOneCompound_Fails()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(PlaceChestCompound(f.State.ContainerIds.PeekNext(), FunctionalBlocks.FreeCell));
        var dig = new TerrainEditOp(SimFixture.Planet, TerrainShape.Sphere, f.Sim.Ground(3), 0.5f, TerrainEditMode.Remove, 0);
        var place = PlaceBlock(FunctionalBlocks.FreeCell2, FunctionalBlocks.ChestType);

        // Two irreversible components, in either order, and two construct components.
        f.AssertFails(new CompoundOp([dig, place]));
        f.AssertFails(new CompoundOp([place, dig]));
        f.AssertFails(new CompoundOp([place, new BlockClearOp(ConstructId, FunctionalBlocks.LampCell)]));
        Assert.Empty(f.Sim.Terrain.AppliedEdits);
    }

    [Fact]
    public void FunctionStateWithBlockComponent_Fails()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(new CompoundOp(
        [
            SetFunctionState(FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 1),
            SetFunctionState(FunctionalBlocks.DoorHighCell, FunctionalBlocks.DoorType, 1),
        ]));
        var state = SetFunctionState(FunctionalBlocks.LampCell, FunctionalBlocks.LampType, 1);
        var place = PlaceBlock(FunctionalBlocks.FreeCell, FunctionalBlocks.ChestType);

        f.AssertFails(new CompoundOp([state, place]));
        f.AssertFails(new CompoundOp([place, state]));
        f.AssertFails(new CompoundOp([state, new BlockClearOp(ConstructId, FunctionalBlocks.FreeCell2)]));
    }

    [Fact]
    public void RemoveChest_Compound_ClearsContainerEntityAndBlock()
    {
        var f = new FunctionalBlockFixture();
        var container = f.SetUpChest(_chestCell);
        var version = f.ConstructVersion;

        var applied = f.Apply(new CompoundOp(
        [
            new ContainerRemoveOp(container),
            ClearState(_chestCell, FunctionalBlocks.ChestType),
            new BlockClearOp(ConstructId, _chestCell),
        ]));

        Assert.False(f.State.Containers.TryGet(container, out _));
        Assert.False(f.HasState(_chestCell, out _));
        Assert.Equal(BlockState.Empty, f.BlockAt(_chestCell));
        Assert.Equal(version + 1, f.ConstructVersion);
        Assert.Equal(new ConstructChange(ConstructId, version + 1, false), Assert.Single(applied.ConstructChanges));
        Assert.True(Assert.Single(applied.ContainerChanges).Removed);
    }

    [Fact]
    public void RemoveLastBlockWithEntity_Compound_RemovesConstruct()
    {
        var f = new FunctionalBlockFixture(withConstruct: false);
        var (_, construct, container) = PlaceChestOnTerrain(f);

        var applied = f.Apply(new CompoundOp(
        [
            new ContainerRemoveOp(container),
            new BlockEntitySetOp(construct, default, FunctionalBlocks.ChestType, null),
            new BlockClearOp(construct, default),
        ]));

        Assert.False(f.State.Constructs.TryGet(construct, out _));
        Assert.False(f.State.Containers.TryGet(container, out _));
        Assert.Equal(0, f.State.BlockEntities.Count);
        Assert.True(Assert.Single(applied.ConstructChanges).Removed);
    }

    [Fact]
    public void BlockClear_WithBlockEntity_Fails()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(SetBedState(FunctionalBlocks.BedCell, PlayerId.None));

        f.AssertFails(new BlockClearOp(ConstructId, FunctionalBlocks.BedCell));
        Assert.True(f.HasState(FunctionalBlocks.BedCell, out _));

        f.Apply(ClearState(FunctionalBlocks.BedCell, FunctionalBlocks.BedType));
        f.Apply(new BlockClearOp(ConstructId, FunctionalBlocks.BedCell));
        Assert.Equal(BlockState.Empty, f.BlockAt(FunctionalBlocks.BedCell));
    }

    [Fact]
    public void BlockClear_WithHostedContainer_Fails()
    {
        var f = new FunctionalBlockFixture();
        var container = f.CreateChestContainer(_chestCell);

        f.AssertFails(new BlockClearOp(ConstructId, _chestCell));
        Assert.Single(f.HostedBy(_chestCell));

        f.Apply(new ContainerRemoveOp(container));
        f.Apply(new BlockClearOp(ConstructId, _chestCell));
        Assert.Equal(BlockState.Empty, f.BlockAt(_chestCell));
    }

    [Fact]
    public void FailingItemComponent_ConstructAndEntitiesUntouched()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(PlaceChestCompound(f.State.ContainerIds.PeekNext(), FunctionalBlocks.FreeCell));
        var hash = StateHasher.Hash(f.State);
        var nextContainer = f.State.ContainerIds.NextValue;
        var version = f.ConstructVersion;
        var entities = f.State.BlockEntities.Count;
        var container = f.State.ContainerIds.PeekNext();
        var stone = WorldFactory.Item(SurvivalContentFactory.StoneKey);
        var compound = new CompoundOp(
        [
            ChestContainer(container, ConstructId, FunctionalBlocks.FreeCell2),
            SetChestState(FunctionalBlocks.FreeCell2, container),
            new ItemDestroyOp(new SlotRef(Bag, 0), stone, 1, ItemSink.BuildCost),
            PlaceBlock(FunctionalBlocks.FreeCell2, FunctionalBlocks.ChestType),
        ]);

        Assert.False(f.TryApplyRaw(compound, out _, out _, out var index));

        Assert.Equal(2, index);
        Assert.Equal(hash, StateHasher.Hash(f.State));
        Assert.Equal(nextContainer, f.State.ContainerIds.NextValue);
        Assert.Equal(version, f.ConstructVersion);
        Assert.Equal(entities, f.State.BlockEntities.Count);
        Assert.False(f.State.Containers.TryGet(container, out _));
        Assert.Equal(BlockState.Empty, f.BlockAt(FunctionalBlocks.FreeCell2));
        f.AssertFails(compound);
    }

    [Fact]
    public void FailedCompound_ThenStandaloneOps_SeeNoStaleView()
    {
        var f = new FunctionalBlockFixture();
        var applier = new OperationApplier(f.State, f.Sim.Terrain);
        var stone = WorldFactory.Item(SurvivalContentFactory.StoneKey);
        var emptySlot = new ItemDestroyOp(new SlotRef(Bag, 0), stone, 1, ItemSink.BuildCost);
        var container = f.State.ContainerIds.PeekNext();
        var doorState = f.BlockAt(FunctionalBlocks.DoorLowCell);

        // Refused on a component after the effective view and a function state were collected (the rollback path), and refused on the
        // condition of the last component (the early exit): neither may leave anything to the next operation of the same applier.
        var afterView = new CompoundOp([emptySlot, PlaceBlock(FunctionalBlocks.FreeCell, FunctionalBlocks.ChestType)]);
        var afterState = new CompoundOp([SetFunctionState(FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 1), emptySlot]);
        var earlyExit = new CompoundOp([ChestContainer(container, ConstructId, FunctionalBlocks.FreeCell), PlaceBlock(_chestCell, FunctionalBlocks.ChestType)]);
        Assert.False(applier.TryApply(LastOp(f).Next, f.State.CurrentTick, null, afterView, out _, out _, out _));
        Assert.False(applier.TryApply(LastOp(f).Next, f.State.CurrentTick, null, afterState, out _, out _, out _));
        Assert.False(applier.TryApply(LastOp(f).Next, f.State.CurrentTick, null, earlyExit, out _, out _, out _));

        // The cell of the refused placement is empty: a container hosted by it, and a state on it, are refused as on a fresh applier.
        Assert.False(applier.TryApply(LastOp(f).Next, f.State.CurrentTick, null, ChestContainer(container, ConstructId, FunctionalBlocks.FreeCell), out _, out _, out _));
        Assert.False(applier.TryApply(LastOp(f).Next, f.State.CurrentTick, null, SetChestState(FunctionalBlocks.FreeCell, container), out _, out _, out _));

        // A harmless operation applied next must not carry the function state of the refused compound.
        Assert.True(applier.TryApply(LastOp(f).Next, f.State.CurrentTick, null, Stone(Bag, 0, 1), out var applied, out _, out _));
        Assert.Empty(applied!.ConstructChanges);
        Assert.Equal(doorState, f.BlockAt(FunctionalBlocks.DoorLowCell));
    }

    private static OpId LastOp(FunctionalBlockFixture f) => f.State.LastOpId;

    [Fact]
    public void DoorColumnCompound_TwoCells_OneVersionBump()
    {
        var f = new FunctionalBlockFixture();
        var version = f.ConstructVersion;

        var applied = f.Apply(new CompoundOp(
        [
            SetFunctionState(FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 1),
            SetFunctionState(FunctionalBlocks.DoorHighCell, FunctionalBlocks.DoorType, 1),
        ]));

        Assert.Equal(version + 1, f.ConstructVersion);
        Assert.Equal(new ConstructChange(ConstructId, version + 1, false), Assert.Single(applied.ConstructChanges));
        Assert.Equal(1, f.BlockAt(FunctionalBlocks.DoorLowCell).FunctionState);
        Assert.Equal(1, f.BlockAt(FunctionalBlocks.DoorHighCell).FunctionState);
    }

    [Fact]
    public void PlaceBedWithOwnerAndRespawn_OneCompound_UsesEffectiveView()
    {
        var f = new FunctionalBlockFixture();
        var cell = FunctionalBlocks.FreeCell;
        var version = f.ConstructVersion;

        // The bed block is the last component, yet operations 40 and 44 see it in the cell (D12, effective view).
        var applied = f.Apply(new CompoundOp(
        [
            SetBedState(cell, Player1),
            new RespawnPointSetOp(Player1, BedAt(cell)),
            PlaceBlock(cell, FunctionalBlocks.BedType),
        ]));

        Assert.Equal(FunctionalBlocks.BedType, f.BlockAt(cell).TypeId);
        Assert.True(f.HasState(cell, out var state));
        Assert.Equal(new BedBlockState(Player1), state);
        Assert.True(f.State.Progress.TryGet(Player1, out var progress));
        Assert.Equal(BedAt(cell), progress.RespawnBed);
        Assert.Equal(version + 1, f.ConstructVersion);
        Assert.Equal(new ConstructChange(ConstructId, version + 1, false), Assert.Single(applied.ConstructChanges));
    }

    [Fact]
    public void FunctionStatesOnTwoConstructs_OneChangePerConstruct()
    {
        var f = new FunctionalBlockFixture();
        var second = f.State.EntityIds.PeekNext();
        f.Apply(new ConstructCreateOp(
            second, FrameRef.Of(WorldFactory.HomePlanet), new Vector3(0, 4000, 0), Quaternion.Identity, default,
            BlockState.Create(FunctionalBlocks.DoorType, 0)));
        var firstVersion = f.ConstructVersion;
        Assert.True(f.State.Constructs.TryGet(second, out var created));
        var secondVersion = created.Version;

        var applied = f.Apply(new CompoundOp(
        [
            SetFunctionState(FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 1),
            new BlockFunctionStateSetOp(second, default, FunctionalBlocks.DoorType, 1),
            SetFunctionState(FunctionalBlocks.DoorHighCell, FunctionalBlocks.DoorType, 1),
        ]));

        Assert.Equal(2, applied.ConstructChanges.Count);
        Assert.Contains(new ConstructChange(ConstructId, firstVersion + 1, false), applied.ConstructChanges);
        Assert.Contains(new ConstructChange(second, secondVersion + 1, false), applied.ConstructChanges);
        Assert.Equal(firstVersion + 1, f.ConstructVersion);
        Assert.True(f.State.Constructs.TryGet(second, out var after));
        Assert.Equal(secondVersion + 1, after.Version);
        Assert.Equal(1, after.GetBlock(default).FunctionState);
    }

    /// <summary>The compound of the plan (D8) for a chest placed on a face of the fixture construct: container, state, block last.</summary>
    internal static CompoundOp PlaceChestCompound(ContainerId container, CellPos cell, ushort size = 8) => new(
    [
        ChestContainer(container, ConstructId, cell, size),
        SetChestState(cell, container),
        PlaceBlock(cell, FunctionalBlocks.ChestType),
    ]);

    /// <summary>A chest placed on the terrain: a new construct with the chest as its only block.</summary>
    private static (AppliedOperation Applied, EntityId ConstructId, ContainerId Container) PlaceChestOnTerrain(FunctionalBlockFixture f)
    {
        var construct = f.State.EntityIds.PeekNext();
        var container = f.State.ContainerIds.PeekNext();
        var applied = f.Apply(new CompoundOp(
        [
            ChestContainer(container, construct, default),
            new BlockEntitySetOp(construct, default, FunctionalBlocks.ChestType, new ChestBlockState(container)),
            new ConstructCreateOp(
                construct, FrameRef.Of(WorldFactory.HomePlanet), new Vector3(0, 4000, 0), Quaternion.Identity, default,
                BlockState.Create(FunctionalBlocks.ChestType, 0)),
        ]));
        return (applied, construct, container);
    }
}
