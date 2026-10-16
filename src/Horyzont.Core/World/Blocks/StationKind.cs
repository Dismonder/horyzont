namespace Horyzont.Core.World.Blocks;

/// <summary>Kind of a crafting station (K6-32, D5). Stations with time are <see cref="Furnace"/> and <see cref="Refinery"/>; behaviour is code, recipes are data.</summary>
public enum StationKind : byte
{
    /// <summary>Crafting in the hands: no station.</summary>
    Hands = 0,

    /// <summary>Workbench: levels 1 to 4 from upgrades.</summary>
    Workbench = 1,

    /// <summary>Furnace: processes input over time.</summary>
    Furnace = 2,

    /// <summary>Refinery: processes input over time.</summary>
    Refinery = 3,

    /// <summary>Kitchen.</summary>
    Kitchen = 4,

    /// <summary>Fabricator.</summary>
    Fabricator = 5,
}
