namespace Horyzont.Core.BlockEntities;

/// <summary>Kind of a block state (spec 03). Format 1 of operations and blobs carries only <see cref="Chest"/> and <see cref="Bed"/>.</summary>
public enum BlockEntityKind : byte
{
    /// <summary>Chest: refers to its container.</summary>
    Chest = 1,

    /// <summary>Bed: refers to its owner.</summary>
    Bed = 2,

    /// <summary>Station; reserved for K6-32.</summary>
    Station = 3,

    /// <summary>Teleport; reserved for K6-37.</summary>
    Teleport = 4,

    /// <summary>Oxygen generator; reserved for K6-34.</summary>
    OxygenGenerator = 5,

    /// <summary>Planter; reserved for K6-38.</summary>
    Planter = 6,

    /// <summary>Cargo hold of a ship; reserved for K6-37.</summary>
    CargoHold = 7,
}
