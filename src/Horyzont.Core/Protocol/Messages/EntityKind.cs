namespace Horyzont.Core.Protocol.Messages;

/// <summary>Kind of an entity. Reserved: Creature = 4, Projectile = 5, Boss = 7 (refused by the codec until a package activates them).</summary>
public enum EntityKind : byte
{
    /// <summary>Player body.</summary>
    Player = 1,

    /// <summary>Construct.</summary>
    Construct = 2,

    /// <summary>Dropped item (K6-35 B6, review P2-1): archetype 0 and empty kind data.</summary>
    DroppedItem = 3,

    /// <summary>Tombstone (K6-35 B6, review P2-1): archetype 0 and empty kind data.</summary>
    Tombstone = 6,
}
