namespace Horyzont.Core.Ids;

/// <summary>Identifier of an entity (player body, construct and later persistent or transient entities).</summary>
/// <param name="Value">Numeric value; 0 is never assigned.</param>
public readonly record struct EntityId(ulong Value)
{
    /// <summary>
    /// First transient identifier. Persistent entities (players, constructs, persistent entities of subproject 3)
    /// use 1 .. 2^32 − 1 from <see cref="EntityIdAllocator"/>. Transient entities (creatures, projectiles, bosses
    /// of subproject 4) use numbers from 2^32, counted by the server outside the checkpoint and the state hash.
    /// </summary>
    public const ulong TransientBase = 1UL << 32;

    /// <summary>The unassigned identifier (0).</summary>
    public static EntityId None => default;

    /// <summary>True for <see cref="None"/>.</summary>
    public bool IsNone => Value == 0;

    /// <summary>True for identifiers of transient entities (at least <see cref="TransientBase"/>).</summary>
    public bool IsTransient => Value >= TransientBase;
}
