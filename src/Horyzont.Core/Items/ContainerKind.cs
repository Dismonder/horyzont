namespace Horyzont.Core.Items;

/// <summary>Kind of a container (spec 03; every value exists from format 1).</summary>
public enum ContainerKind : byte
{
    /// <summary>Backpack of a player.</summary>
    PlayerBag = 1,

    /// <summary>Hotbar of a player.</summary>
    Hotbar = 2,

    /// <summary>Equipment of a player.</summary>
    Equipment = 3,

    /// <summary>Chest block.</summary>
    Chest = 4,

    /// <summary>Cargo hold of a ship.</summary>
    CargoHold = 5,

    /// <summary>Input of a station.</summary>
    StationInput = 6,

    /// <summary>Output of a station.</summary>
    StationOutput = 7,

    /// <summary>Tombstone after a death.</summary>
    Tombstone = 8,

    /// <summary>A dropped item lying in the world.</summary>
    DroppedItem = 9,
}
