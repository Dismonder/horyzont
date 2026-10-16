using Horyzont.Core.BlockEntities;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.BlockEntities;

/// <summary>
/// A <see cref="SimFixture"/> over <see cref="WorldFactory.CreateWithFunctionalBlocks"/> with helpers for the operations of K6-30.
/// Setup helpers apply the operations through <see cref="ServerSimulation.ApplyServerOperation"/>, so they throw until the behaviour exists.
/// </summary>
internal sealed class FunctionalBlockFixture
{
    public FunctionalBlockFixture(int players = 1, bool withConstruct = true)
    {
        Sim = new SimFixture(state: WorldFactory.CreateWithFunctionalBlocks(players, withConstruct));
    }

    public static PlayerId Player1 => new(1);

    public static PlayerId Player2 => new(2);

    public static EntityId ConstructId => FunctionalBlocks.Construct;

    public static ContainerId Bag => WorldFactory.BagOf(1);

    public SimFixture Sim { get; }

    public Horyzont.Core.World.WorldState State => Sim.State;

    public uint ConstructVersion => State.Constructs.TryGet(ConstructId, out var construct) ? construct.Version : 0U;

    public BlockState BlockAt(CellPos cell) => State.Constructs.TryGet(ConstructId, out var construct) ? construct.GetBlock(cell) : BlockState.Empty;

    public AppliedOperation Apply(Operation op) => Sim.Sim.ApplyServerOperation(op);

    /// <summary>The operation is refused: it throws, the hash, LastOpId and journal do not change and the simulation is not faulted.</summary>
    public void AssertFails(Operation op)
    {
        var hash = StateHasher.Hash(State);
        var last = State.LastOpId;
        var journaled = Sim.Journal.Entries.Count;

        Assert.Throws<InvalidOperationException>(() => Sim.Sim.ApplyServerOperation(op));

        Assert.Equal(hash, StateHasher.Hash(State));
        Assert.Equal(last, State.LastOpId);
        Assert.Equal(journaled, Sim.Journal.Entries.Count);
        Assert.False(Sim.Sim.IsFaulted);
    }

    /// <summary>
    /// Runs the applier directly, without journaling; use it only for operations that are expected to fail (a success would leave
    /// <see cref="Horyzont.Core.World.WorldState.LastOpId"/> ahead of the journal).
    /// </summary>
    public bool TryApplyRaw(Operation op, out AppliedOperation? applied, out string failure, out int componentIndex) =>
        new OperationApplier(State, Sim.Terrain).TryApply(State.LastOpId.Next, State.CurrentTick, null, op, out applied, out failure, out componentIndex);

    public Container Container(ContainerId id) =>
        State.Containers.TryGet(id, out var container) ? container : throw new InvalidOperationException($"No container {id.Value}.");

    /// <summary>Containers hosted by the block in the cell of the fixture construct.</summary>
    public IEnumerable<Container> HostedBy(CellPos cell) =>
        State.Containers.All.Where(c => c.Host.Kind == ContainerHostKind.ConstructBlock && c.Host.Id == ConstructId.Value && c.Host.Cell == cell);

    public static ContainerCreateOp ChestContainer(ContainerId id, EntityId construct, CellPos cell, ushort size = 4, ContainerAccess access = ContainerAccess.Shared) =>
        new(id, ContainerKind.Chest, ContainerHost.ConstructBlock(construct, cell), Player1, access, size);

    /// <summary>Creates a chest container hosted by the block in <paramref name="cell"/> (a plain chest block of the construct).</summary>
    public ContainerId CreateChestContainer(CellPos cell, ushort size = 4, ContainerAccess access = ContainerAccess.Shared)
    {
        var id = State.ContainerIds.PeekNext();
        Apply(ChestContainer(id, ConstructId, cell, size, access));
        return id;
    }

    /// <summary>Creates a chest container and the state that refers to it for the chest block in <paramref name="cell"/>.</summary>
    public ContainerId SetUpChest(CellPos cell, ushort size = 4, ContainerAccess access = ContainerAccess.Shared)
    {
        var id = CreateChestContainer(cell, size, access);
        Apply(SetChestState(cell, id));
        return id;
    }

    public static BlockEntitySetOp SetChestState(CellPos cell, ContainerId container) =>
        new(ConstructId, cell, FunctionalBlocks.ChestType, new ChestBlockState(container));

    public static BlockEntitySetOp SetBedState(CellPos cell, PlayerId owner) =>
        new(ConstructId, cell, FunctionalBlocks.BedType, new BedBlockState(owner));

    public static BlockEntitySetOp ClearState(CellPos cell, ushort type) => new(ConstructId, cell, type, null);

    public static BedRef BedAt(CellPos cell) => new(ConstructId, cell, FunctionalBlocks.BedType);

    public static BlockFunctionStateSetOp SetFunctionState(CellPos cell, ushort type, byte state) => new(ConstructId, cell, type, state);

    public bool HasState(CellPos cell, out BlockEntityState? state) => State.BlockEntities.TryGet(new BlockRef(ConstructId, cell), out state);

    public static BlockSetOp PlaceBlock(CellPos cell, ushort type) => new(ConstructId, cell, BlockState.Create(type, 0));

    public static ItemCreateOp Stone(ContainerId container, int slot, int count) =>
        new(new SlotRef(container, (ushort)slot), WorldFactory.Item(SurvivalContentFactory.StoneKey), (ushort)count, 500, ItemSource.Admin);
}
