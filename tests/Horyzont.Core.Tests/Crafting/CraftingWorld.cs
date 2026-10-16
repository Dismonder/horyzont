using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Crafting;

/// <summary>
/// Setup helpers for the intent tests of K6-32 stage B over <see cref="FunctionalBlockFixture.ForCrafting"/>. Setup goes through server operations,
/// so the helpers that discover items skip the types a player already discovered: with recipe content the simulation discovers held items by itself
/// (D9b), without it the helpers do it explicitly.
/// </summary>
internal static class CraftingWorld
{
    /// <summary>Cell of a workbench; its neighbours (0,1,2) and (2,1,2) are 1 m away, (1,1,1) and (1,1,3) too.</summary>
    public static readonly CellPos BenchCell = new(1, 1, 2);

    /// <summary>Cell of a station with time put up by a server operation.</summary>
    public static readonly CellPos FurnaceCell = FunctionalBlocks.FreeCell;

    /// <summary>A second cell for a station with time.</summary>
    public static readonly CellPos FurnaceCell2 = FunctionalBlocks.FreeCell2;

    /// <summary>The cell a station put up by an intent lands in: the top of the floor block at x = 4.</summary>
    public static readonly CellPos IntentCell = new(4, 1, 0);

    /// <summary>The construct of the first station on the other planet.</summary>
    public static readonly EntityId OtherPlanetConstruct = new(102);

    /// <summary>A second construct on the home planet.</summary>
    public static readonly EntityId NearConstruct = new(101);

    public static PlayerId Player1 => FunctionalBlockFixture.Player1;

    public static PlayerId Player2 => FunctionalBlockFixture.Player2;

    public static ContainerId Bag => FunctionalBlockFixture.Bag;

    public static ContainerId Hotbar => FunctionalBlockFixture.Hotbar;

    public static EntityId ConstructId => FunctionalBlockFixture.ConstructId;

    public static FunctionalBlockFixture ForCrafting(int players = 1, bool withRecipes = true, bool secondPlanet = false, GameRules? rules = null) =>
        FunctionalBlockFixture.ForCrafting(players, withRecipes, secondPlanet, rules);

    public static ItemTypeId Tool => WorldFactory.Item(SurvivalContentFactory.ToolKey);

    public static ItemTypeId Gem => WorldFactory.Item(SurvivalContentFactory.GemKey);

    public static ItemTypeId Stone => WorldFactory.Item(SurvivalContentFactory.StoneKey);

    public static ItemTypeId Soil => WorldFactory.Item(SurvivalContentFactory.SoilKey);

    public static ItemTypeId Ore => WorldFactory.Item(SurvivalContentFactory.OreKey);

    public static ItemTypeId Peat => WorldFactory.Item(SurvivalContentFactory.PeatKey);

    public static ItemTypeId Brick => WorldFactory.Item(SurvivalContentFactory.BrickKey);

    public static ItemTypeId Ingot => WorldFactory.Item(SurvivalContentFactory.IngotKey);

    public static ItemTypeId Plate => WorldFactory.Item(SurvivalContentFactory.PlateKey);

    public static ItemTypeId FuelCell => WorldFactory.Item(SurvivalContentFactory.FuelCellKey);

    public static ItemTypeId Stew => WorldFactory.Item(SurvivalContentFactory.StewKey);

    public static ItemTypeId Module => WorldFactory.Item(SurvivalContentFactory.ModuleKey);

    /// <summary>All item types of the crafting world.</summary>
    public static IReadOnlyList<ItemTypeId> AllItems { get; } =
        [.. SurvivalContentFactory.CraftingItemKeys.Select(WorldFactory.Item)];

    /// <summary>The stack limit of a type in the test content (the tool 1, the gem and the ingot 5, the rest 500).</summary>
    public static ushort StackLimit(ItemTypeId type) => type == Tool ? (ushort)1 : (type == Gem || type == Ingot) ? (ushort)5 : (ushort)500;

    /// <summary>Block reference of a block of the fixture construct.</summary>
    public static BlockRef At(CellPos cell) => new(ConstructId, cell);

    /// <summary>A craft intent; <paramref name="station"/> is a cell of the fixture construct, null for the hands.</summary>
    public static CraftIntent Craft(string recipe, int count = 1, CellPos? station = null) =>
        new(recipe, (ushort)count, station is { } cell ? At(cell) : null);

    public static ItemCreateOp Create(ContainerId container, int slot, ItemTypeId type, int count) =>
        new(new SlotRef(container, (ushort)slot), type, (ushort)count, StackLimit(type), ItemSource.Admin);

    /// <summary>Puts <paramref name="count"/> items into a slot by a server operation.</summary>
    public static void Stock(this FunctionalBlockFixture f, ContainerId container, int slot, ItemTypeId type, int count) =>
        f.Apply(Create(container, slot, type, count));

    /// <summary>A chest block in <paramref name="cell"/> with its container (4 slots), owned by <paramref name="owner"/>, holding the items from slot 0 on.</summary>
    public static ContainerId SetUpChestWith(
        this FunctionalBlockFixture f, CellPos cell, PlayerId owner, ContainerAccess access, params (ItemTypeId Type, int Count)[] items)
    {
        var chest = f.SetUpChestAt(cell, owner, access);
        for (var i = 0; i < items.Length; i++)
        {
            f.Stock(chest, i, items[i].Type, items[i].Count);
        }

        return chest;
    }

    /// <summary>Discovers the types the player has not discovered yet (server operations).</summary>
    public static void Discover(this FunctionalBlockFixture f, PlayerId player, params ItemTypeId[] types)
    {
        foreach (var type in types)
        {
            if (!f.IsDiscovered(player, type))
            {
                f.Apply(new ItemDiscoverOp(player, type));
            }
        }
    }

    /// <summary>Discovers every item type of the crafting world for the player.</summary>
    public static void DiscoverAll(this FunctionalBlockFixture f, PlayerId player) => f.Discover(player, [.. AllItems]);

    public static bool IsDiscovered(this FunctionalBlockFixture f, PlayerId player, ItemTypeId type) =>
        f.State.Progress.TryGet(player, out var progress) && progress.DiscoveredItems.Contains(type);

    /// <summary>
    /// Puts up a station with time (D18) by one server compound: the input and the output container (the second id is the first plus one), the state and
    /// the block last. A furnace by default; the containers have <see cref="CraftingBlocks.InputSlots"/> and <see cref="CraftingBlocks.OutputSlots"/> slots.
    /// </summary>
    public static (ContainerId Input, ContainerId Output) SetUpTimedStation(
        this FunctionalBlockFixture f, CellPos cell, string recipe = "", Tick? started = null, ushort type = CraftingBlocks.FurnaceType)
    {
        var input = f.State.ContainerIds.PeekNext();
        var output = new ContainerId(input.Value + 1);
        var host = ContainerHost.ConstructBlock(ConstructId, cell);
        f.Apply(new CompoundOp(
        [
            new ContainerCreateOp(input, ContainerKind.StationInput, host, Player1, ContainerAccess.Shared, CraftingBlocks.InputSlots),
            new ContainerCreateOp(output, ContainerKind.StationOutput, host, Player1, ContainerAccess.Shared, CraftingBlocks.OutputSlots),
            new BlockEntitySetOp(ConstructId, cell, type, new StationBlockState(input, output, recipe, started)),
            PlaceBlock(cell, type),
        ]));
        return (input, output);
    }

    /// <summary>The state of the station in <paramref name="cell"/>.</summary>
    public static StationBlockState StationAt(this FunctionalBlockFixture f, CellPos cell)
    {
        Assert.True(f.HasState(cell, out var state));
        return Assert.IsType<StationBlockState>(state);
    }

    /// <summary>The number of items of <paramref name="type"/> in all slots of a container.</summary>
    public static int CountIn(this FunctionalBlockFixture f, ContainerId container, ItemTypeId type)
    {
        var sum = 0;
        var c = f.Container(container);
        for (var i = 0; i < c.Size; i++)
        {
            var stack = c.GetSlot(i);
            sum += stack.Type == type ? stack.Count : 0;
        }

        return sum;
    }

    /// <summary>The operations of the journal entries from index <paramref name="from"/> on.</summary>
    public static List<Operation> JournalFrom(this FunctionalBlockFixture f, int from) =>
        [.. f.Sim.Journal.Entries.Skip(from).Select(e => e.Operation)];

    /// <summary>The number of journal entries.</summary>
    public static int JournalCount(this FunctionalBlockFixture f) => f.Sim.Journal.Entries.Count;

    /// <summary>Creates a construct with one block by a server operation.</summary>
    public static void AddConstruct(this FunctionalBlockFixture f, EntityId id, FrameRef frame, Vector3 origin, CellPos cell, ushort type) =>
        f.Apply(new ConstructCreateOp(id, frame, origin, Quaternion.Identity, cell, BlockState.Create(type, 0)));

    /// <summary>Advances the simulation by <paramref name="ticks"/> ticks.</summary>
    public static void Wait(this FunctionalBlockFixture f, int ticks) => f.Sim.Ticks(ticks);
}
