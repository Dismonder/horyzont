using Horyzont.Core.Ids;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>
/// An entity appears for the client; a spawn of a known entity replaces the client copy. Players and constructs have
/// Archetype 0; constructs have empty KindData, players their seat (<see cref="EntitySpawnSeat"/>, K5-26: empty when not seated).
/// </summary>
/// <param name="Entity">Entity.</param>
/// <param name="Kind">Entity kind.</param>
/// <param name="Archetype">Archetype defined by the entity kind.</param>
/// <param name="Owner">Owning player.</param>
/// <param name="Frame">Frame of <paramref name="Initial"/> (its FrameIndex is 0).</param>
/// <param name="Initial">Initial state.</param>
/// <param name="KindData">Kind-specific data, at most 64 bytes.</param>
#pragma warning disable CA1819 // Message records own their arrays (decoders copy them).
public sealed record EntitySpawn(
    EntityId Entity, EntityKind Kind, ushort Archetype, PlayerId? Owner, SnapshotFrame Frame, EntityState Initial, byte[] KindData) : Message
#pragma warning restore CA1819
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.EntitySpawn;
}
