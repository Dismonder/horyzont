using Horyzont.Core.Ids;

namespace Horyzont.Core.Progress;

/// <summary>
/// Set of item types a player discovered (K6-32, D7): an immutable bit set with equality by content. Canonical bytes: bit i in byte i / 8, least
/// significant bit first, no trailing zero bytes, bit 0 always clear (0 means no type).
/// </summary>
public sealed class ItemTypeBitSet : IEquatable<ItemTypeBitSet>
{
    /// <summary>Gets the empty set.</summary>
    public static ItemTypeBitSet Empty => throw new NotImplementedException("K6-32 A2");

    /// <summary>Gets the number of types in the set.</summary>
    public int Count => throw new NotImplementedException("K6-32 A2");

    /// <summary>Gets a value indicating whether the set has no type.</summary>
    public bool IsEmpty => throw new NotImplementedException("K6-32 A2");

    /// <summary>Gets the canonical bytes.</summary>
    public ReadOnlySpan<byte> Bytes => throw new NotImplementedException("K6-32 A2");

    /// <summary>Creates a set from canonical bytes.</summary>
    /// <param name="bytes">Bytes; a trailing zero byte, bit 0 or more than 512 bytes are refused.</param>
    /// <returns>The set.</returns>
    public static ItemTypeBitSet FromBytes(ReadOnlySpan<byte> bytes) => throw new NotImplementedException("K6-32 A2");

    /// <summary>Whether the set has the type.</summary>
    /// <param name="type">The type.</param>
    /// <returns>True when it is in the set.</returns>
    public bool Contains(ItemTypeId type) => throw new NotImplementedException("K6-32 A2");

    /// <summary>The set with the type added.</summary>
    /// <param name="type">The type, not 0.</param>
    /// <returns>A new set, or this one when it already has the type.</returns>
    public ItemTypeBitSet With(ItemTypeId type) => throw new NotImplementedException("K6-32 A2");

    /// <summary>The set with the type removed (undo of a discovery).</summary>
    /// <param name="type">The type.</param>
    /// <returns>A new set, or this one when it does not have the type.</returns>
    public ItemTypeBitSet Without(ItemTypeId type) => throw new NotImplementedException("K6-32 A2");

    /// <inheritdoc />
    public bool Equals(ItemTypeBitSet? other) => throw new NotImplementedException("K6-32 A2");

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ItemTypeBitSet);

    /// <inheritdoc />
    public override int GetHashCode() => throw new NotImplementedException("K6-32 A2");
}
