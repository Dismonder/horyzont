namespace Horyzont.Core.World;

/// <summary>Kind of a persistent entity; the values are those of <c>EntityKind</c> in the protocol.</summary>
public enum PersistentEntityKind : byte
{
    /// <summary>An item lying in the world (K6-36).</summary>
    DroppedItem = 3,

    /// <summary>The tombstone of a dead player (K6-35).</summary>
    Tombstone = 6,
}
