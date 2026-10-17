namespace Horyzont.Core.Items;

/// <summary>Kind of the thing a container belongs to (spec 03; every value exists from format 1).</summary>
public enum ContainerHostKind : byte
{
    /// <summary>A player (bag, hotbar, equipment).</summary>
    Player = 1,

    /// <summary>A block of a construct (chest, station, cargo hold).</summary>
    ConstructBlock = 2,

    /// <summary>A persistent entity (dropped item, tombstone).</summary>
    PersistentEntity = 3,

    /// <summary>A chest of a place of interest of subproject 5: planet, site and number.</summary>
    Poi = 4,
}
