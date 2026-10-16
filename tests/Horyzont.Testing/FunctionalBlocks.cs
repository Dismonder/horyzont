using Horyzont.Core.Ids;
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
}
