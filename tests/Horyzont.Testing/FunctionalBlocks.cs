using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Testing;

/// <summary>Block types and cells of the construct of <see cref="WorldFactory.CreateWithFunctionalBlocks"/> (K6-30).</summary>
public static class FunctionalBlocks
{
    /// <summary>Block type keys; the registry ids are 1..5 in this order (core:missing is 6).</summary>
    public static IReadOnlyList<string> BlockKeys { get; } =
        [WorldFactory.StoneBlockKey, "core:chest_wood", "core:bed", "core:lamp", "core:wood_door"];

    /// <summary>Id of <c>core:stone_block</c>.</summary>
    public const ushort StoneType = 1;

    /// <summary>Id of <c>core:chest_wood</c>.</summary>
    public const ushort ChestType = 2;

    /// <summary>Id of <c>core:bed</c>.</summary>
    public const ushort BedType = 3;

    /// <summary>Id of <c>core:lamp</c>.</summary>
    public const ushort LampType = 4;

    /// <summary>Id of <c>core:wood_door</c>.</summary>
    public const ushort DoorType = 5;

    /// <summary>The construct of the world, created after the players joined; the id is above the ids of the players.</summary>
    public static readonly EntityId Construct = new(100);

    /// <summary>Stone floor, y = 0.</summary>
    public static readonly CellPos[] FloorCells = [new(0, 0, 0), new(1, 0, 0), new(2, 0, 0), new(3, 0, 0), new(4, 0, 0)];

    /// <summary>A plain chest block.</summary>
    public static readonly CellPos ChestCell = new(0, 1, 0);

    /// <summary>A plain bed block.</summary>
    public static readonly CellPos BedCell = new(1, 1, 0);

    /// <summary>A plain lamp block (function state 0).</summary>
    public static readonly CellPos LampCell = new(2, 1, 0);

    /// <summary>Lower block of the door column (function state 0 = closed).</summary>
    public static readonly CellPos DoorLowCell = new(3, 1, 0);

    /// <summary>Upper block of the door column.</summary>
    public static readonly CellPos DoorHighCell = new(3, 2, 0);

    /// <summary>An empty cell next to the blocks, for placing more blocks.</summary>
    public static readonly CellPos FreeCell = new(5, 1, 0);

    /// <summary>A second empty cell.</summary>
    public static readonly CellPos FreeCell2 = new(6, 1, 0);

    /// <summary>Key of the chest block type.</summary>
    public const string ChestKey = "core:chest_wood";

    /// <summary>Key of the bed block type.</summary>
    public const string BedKey = "core:bed";

    /// <summary>Key of the lamp block type.</summary>
    public const string LampKey = "core:lamp";

    /// <summary>Key of the door block type.</summary>
    public const string DoorKey = "core:wood_door";

    /// <summary>The block definitions of <see cref="BlockKeys"/> (a stone block, a chest with <paramref name="chestSlots"/> slots, a bed, a lamp and a door; K6-30 D5).</summary>
    /// <param name="chestSlots">Slots of the container of a chest.</param>
    /// <returns>The definitions by key, for <c>ServerRuntimeOptions.BlockDefinitions</c> or <c>BlockContentTable.Create</c>.</returns>
    public static IReadOnlyDictionary<string, BlockTypeDef> DefinitionsFor(ushort chestSlots = 4)
    {
        (string Key, float Mass, BlockFunction Function)[] all =
        [
            (WorldFactory.StoneBlockKey, 2400f, BlockFunction.None),
            (ChestKey, 80f, BlockFunction.CreateChest(chestSlots)),
            (BedKey, 120f, BlockFunction.Bed),
            (LampKey, 20f, BlockFunction.Light),
            (DoorKey, 100f, BlockFunction.Door),
        ];
        var definitions = new Dictionary<string, BlockTypeDef>(StringComparer.Ordinal);
        foreach (var (key, mass, function) in all)
        {
            definitions.Add(key, new BlockTypeDef(key, BlockShape.Cube, 1, true, mass, function));
        }

        return definitions;
    }

    /// <summary>
    /// The content table of a world created by <see cref="WorldFactory.CreateWithFunctionalBlocks"/>: a stone block, a chest with
    /// <paramref name="chestSlots"/> slots, a bed, a lamp and a door (K6-30 D5).
    /// </summary>
    /// <param name="state">The world.</param>
    /// <param name="chestSlots">Slots of the container of a chest.</param>
    /// <returns>The table.</returns>
    public static BlockContentTable ContentFor(WorldState state, ushort chestSlots = 4)
    {
        ArgumentNullException.ThrowIfNull(state);
        return BlockContentTable.Create(state.BlockTypes, DefinitionsFor(chestSlots));
    }
}
