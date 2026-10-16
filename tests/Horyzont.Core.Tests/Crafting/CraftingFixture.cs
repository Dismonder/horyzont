using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.Tests.Items;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Crafting;

/// <summary>
/// A <see cref="FunctionalBlockFixture"/> with helpers for the operations of K6-32 stage A. The applier does not read content, so a station is a
/// block of any type: these tests use the stone block type for it (<see cref="StationBlockType"/>); the block types of stations arrive with the content in stage B.
/// </summary>
internal sealed class CraftingFixture
{
    /// <summary>The block type that holds the station state in these tests.</summary>
    public const ushort StationBlockType = FunctionalBlocks.StoneType;

    public CraftingFixture(int players = 1)
    {
        Blocks = new FunctionalBlockFixture(players);
    }

    /// <summary>Cell of the first station.</summary>
    public static CellPos StationCell => FunctionalBlocks.FreeCell;

    /// <summary>Cell of the second station.</summary>
    public static CellPos StationCell2 => FunctionalBlocks.FreeCell2;

    public static PlayerId Player1 => FunctionalBlockFixture.Player1;

    public static PlayerId Player2 => FunctionalBlockFixture.Player2;

    public static ItemTypeId Stone => ItemFixture.Stone;

    public static ItemTypeId Soil => ItemFixture.Soil;

    public static ItemTypeId Gem => ItemFixture.Gem;

    public static ItemTypeId Tool => ItemFixture.Tool;

    public FunctionalBlockFixture Blocks { get; }

    public WorldState State => Blocks.State;

    public AppliedOperation Apply(Operation op) => Blocks.Apply(op);

    /// <summary>The operation is refused: it throws and changes nothing (hash, last operation, journal).</summary>
    public void AssertFails(Operation op) => Blocks.AssertFails(op);

    public static ItemDiscoverOp Discover(PlayerId player, ItemTypeId type) => new(player, type);

    public static ProgressKeyGrantOp Grant(string key) => new(key);

    public static ContainerCreateOp StationContainer(ContainerId id, ContainerKind kind, CellPos cell, ushort size = 4) =>
        new(id, kind, ContainerHost.ConstructBlock(FunctionalBlockFixture.ConstructId, cell), Player1, ContainerAccess.Shared, size);

    public static BlockEntitySetOp StationState(CellPos cell, ContainerId input, ContainerId output, string recipe = "", Tick? started = null) =>
        new(FunctionalBlockFixture.ConstructId, cell, StationBlockType, new StationBlockState(input, output, recipe, started));

    /// <summary>The compound that places a station with time (D18): two containers, the state, and the block last. The second id is the first plus one.</summary>
    public CompoundOp PlaceStation(CellPos cell, string recipe = "", Tick? started = null, ushort inputSlots = 4, ushort outputSlots = 2)
    {
        var input = State.ContainerIds.PeekNext();
        var output = new ContainerId(input.Value + 1);
        return new CompoundOp(
        [
            StationContainer(input, ContainerKind.StationInput, cell, inputSlots),
            StationContainer(output, ContainerKind.StationOutput, cell, outputSlots),
            StationState(cell, input, output, recipe, started),
            FunctionalBlockFixture.PlaceBlock(cell, StationBlockType),
        ]);
    }

    /// <summary>The compound that removes a station whose containers are empty (D18).</summary>
    public static CompoundOp RemoveStation(CellPos cell, ContainerId input, ContainerId output) => new(
    [
        new ContainerRemoveOp(input),
        new ContainerRemoveOp(output),
        FunctionalBlockFixture.ClearState(cell, StationBlockType),
        new BlockClearOp(FunctionalBlockFixture.ConstructId, cell),
    ]);

    /// <summary>Places a station by <see cref="PlaceStation"/> and returns its containers.</summary>
    public (ContainerId Input, ContainerId Output) SetUpStation(CellPos cell, string recipe = "", Tick? started = null)
    {
        Apply(PlaceStation(cell, recipe, started));
        return StationContainersOf(cell);
    }

    /// <summary>The containers of the station state in <paramref name="cell"/>.</summary>
    public (ContainerId Input, ContainerId Output) StationContainersOf(CellPos cell)
    {
        Assert.True(Blocks.HasState(cell, out var state));
        var station = Assert.IsType<StationBlockState>(state);
        return (station.Input, station.Output);
    }

    public ItemStack Slot(ContainerId container, int slot) => Blocks.Container(container).GetSlot(slot);
}
