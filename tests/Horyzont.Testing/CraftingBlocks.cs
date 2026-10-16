using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Testing;

/// <summary>
/// Block types of the world of <see cref="WorldFactory.CreateWithCrafting"/> (K6-32): the five of <see cref="FunctionalBlocks"/> (ids 1..5) and
/// the stations and the upgrades of a workbench (ids 6..13). The furnace and the refinery have 4 input and 2 output slots.
/// </summary>
public static class CraftingBlocks
{
    /// <summary>Id of <c>core:workbench</c>.</summary>
    public const ushort WorkbenchType = 6;

    /// <summary>Id of <c>core:workbench_anvil</c>, an upgrade of the workbench.</summary>
    public const ushort AnvilType = 7;

    /// <summary>Id of <c>core:workbench_toolrack</c>, an upgrade of the workbench.</summary>
    public const ushort ToolrackType = 8;

    /// <summary>Id of <c>core:workbench_grindstone</c>, an upgrade of the workbench.</summary>
    public const ushort GrindstoneType = 9;

    /// <summary>Id of <c>core:furnace</c>.</summary>
    public const ushort FurnaceType = 10;

    /// <summary>Id of <c>core:refinery</c>.</summary>
    public const ushort RefineryType = 11;

    /// <summary>Id of <c>core:kitchen</c>.</summary>
    public const ushort KitchenType = 12;

    /// <summary>Id of <c>core:fabricator</c>.</summary>
    public const ushort FabricatorType = 13;

    /// <summary>Input slots of the furnace and the refinery.</summary>
    public const ushort InputSlots = 4;

    /// <summary>Output slots of the furnace and the refinery.</summary>
    public const ushort OutputSlots = 2;

    /// <summary>Block type keys; the registry ids are 1..13 in this order (core:missing is 14).</summary>
    public static IReadOnlyList<string> BlockKeys { get; } =
    [
        .. FunctionalBlocks.BlockKeys,
        "core:workbench", "core:workbench_anvil", "core:workbench_toolrack", "core:workbench_grindstone",
        "core:furnace", "core:refinery", "core:kitchen", "core:fabricator",
    ];

    /// <summary>The block definitions of <see cref="BlockKeys"/> (K6-32 D6).</summary>
    /// <param name="chestSlots">Slots of the container of a chest.</param>
    /// <returns>The definitions by key.</returns>
    public static IReadOnlyDictionary<string, BlockTypeDef> DefinitionsFor(ushort chestSlots = 4)
    {
        var definitions = new Dictionary<string, BlockTypeDef>(FunctionalBlocks.DefinitionsFor(chestSlots), StringComparer.Ordinal);
        (string Key, float Mass, BlockFunction Function)[] added =
        [
            ("core:workbench", 150f, BlockFunction.CreateStation(StationKind.Workbench)),
            ("core:workbench_anvil", 200f, BlockFunction.CreateStationUpgrade(StationKind.Workbench)),
            ("core:workbench_toolrack", 60f, BlockFunction.CreateStationUpgrade(StationKind.Workbench)),
            ("core:workbench_grindstone", 120f, BlockFunction.CreateStationUpgrade(StationKind.Workbench)),
            ("core:furnace", 800f, BlockFunction.CreateStation(StationKind.Furnace, InputSlots, OutputSlots)),
            ("core:refinery", 900f, BlockFunction.CreateStation(StationKind.Refinery, InputSlots, OutputSlots)),
            ("core:kitchen", 100f, BlockFunction.CreateStation(StationKind.Kitchen)),
            ("core:fabricator", 500f, BlockFunction.CreateStation(StationKind.Fabricator)),
        ];
        foreach (var (key, mass, function) in added)
        {
            definitions.Add(key, new BlockTypeDef(key, BlockShape.Cube, 1, true, mass, function));
        }

        return definitions;
    }

    /// <summary>The content table of a world created by <see cref="WorldFactory.CreateWithCrafting"/>.</summary>
    /// <param name="state">The world.</param>
    /// <param name="chestSlots">Slots of the container of a chest.</param>
    /// <returns>The table.</returns>
    public static BlockContentTable ContentFor(WorldState state, ushort chestSlots = 4)
    {
        ArgumentNullException.ThrowIfNull(state);
        return BlockContentTable.Create(state.BlockTypes, DefinitionsFor(chestSlots));
    }
}
