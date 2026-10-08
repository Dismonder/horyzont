namespace Horyzont.Core.Protocol.Messages;

/// <summary>Kind of an entity. Reserved: DroppedItem = 3, Creature = 4, Projectile = 5, Tombstone = 6, Boss = 7.</summary>
public enum EntityKind : byte
{
    /// <summary>Player body.</summary>
    Player = 1,

    /// <summary>Construct.</summary>
    Construct = 2,
}
