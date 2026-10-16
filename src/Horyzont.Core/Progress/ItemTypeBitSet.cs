using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;

namespace Horyzont.Core.Progress;

/// <summary>
/// Set of item types a player discovered (K6-32, D7): an immutable bit set with equality by content. Canonical bytes: bit i in byte i / 8, least
/// significant bit first, no trailing zero bytes, bit 0 always clear (0 means no type). Type ids run 1 to <see cref="ProtocolConstants.MaxItemTypes"/>,
/// so the longest set is <see cref="ProtocolConstants.MaxDiscoveredBytes"/> bytes.
/// </summary>
public sealed class ItemTypeBitSet : IEquatable<ItemTypeBitSet>
{
    private readonly byte[] _bytes;

    private ItemTypeBitSet(byte[] bytes)
    {
        _bytes = bytes;
    }

    /// <summary>Gets the empty set.</summary>
    public static ItemTypeBitSet Empty { get; } = new([]);

    /// <summary>Gets the number of types in the set.</summary>
    public int Count
    {
        get
        {
            var count = 0;
            foreach (var b in _bytes)
            {
                count += BitOperations.PopCount(b);
            }

            return count;
        }
    }

    /// <summary>Gets a value indicating whether the set has no type.</summary>
    public bool IsEmpty => _bytes.Length == 0;

    /// <summary>Gets the canonical bytes.</summary>
    public ReadOnlySpan<byte> Bytes => _bytes;

    /// <summary>Creates a set from canonical bytes.</summary>
    /// <param name="bytes">Bytes; a trailing zero byte, bit 0 or more than <see cref="ProtocolConstants.MaxDiscoveredBytes"/> bytes are refused.</param>
    /// <returns>The set.</returns>
    public static ItemTypeBitSet FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return Empty;
        }

        if (bytes.Length > ProtocolConstants.MaxDiscoveredBytes)
        {
            throw new ArgumentException($"A discovery set has at most {ProtocolConstants.MaxDiscoveredBytes} bytes.", nameof(bytes));
        }

        if (bytes[^1] == 0)
        {
            throw new ArgumentException("A discovery set has no trailing zero byte.", nameof(bytes));
        }

        if ((bytes[0] & 1) != 0)
        {
            throw new ArgumentException("Bit 0 stands for no item type.", nameof(bytes));
        }

        if (bytes.Length == ProtocolConstants.MaxDiscoveredBytes && (bytes[^1] & 0xFE) != 0)
        {
            throw new ArgumentException("Only type 4096 fits into the last byte of a discovery set.", nameof(bytes));
        }

        return new ItemTypeBitSet(bytes.ToArray());
    }

    /// <summary>Whether the set has the type.</summary>
    /// <param name="type">The type.</param>
    /// <returns>True when it is in the set.</returns>
    public bool Contains(ItemTypeId type)
    {
        var index = (int)(type.Value >> 3);
        return type.Value is >= 1 and <= ProtocolConstants.MaxItemTypes && index < _bytes.Length && (_bytes[index] & (1 << (int)(type.Value & 7))) != 0;
    }

    /// <summary>The set with the type added.</summary>
    /// <param name="type">The type, 1 to <see cref="ProtocolConstants.MaxItemTypes"/>.</param>
    /// <returns>A new set, or this one when it already has the type.</returns>
    public ItemTypeBitSet With(ItemTypeId type)
    {
        if (type.Value is < 1 or > ProtocolConstants.MaxItemTypes)
        {
            throw new ArgumentOutOfRangeException(nameof(type), type.Value, "An item type is 1 to MaxItemTypes.");
        }

        if (Contains(type))
        {
            return this;
        }

        var index = (int)(type.Value >> 3);
        var bytes = new byte[Math.Max(_bytes.Length, index + 1)];
        _bytes.CopyTo(bytes, 0);
        bytes[index] |= (byte)(1 << (int)(type.Value & 7));
        return new ItemTypeBitSet(bytes);
    }

    /// <summary>The set with the type removed (undo of a discovery).</summary>
    /// <param name="type">The type.</param>
    /// <returns>A new set, or this one when it does not have the type.</returns>
    public ItemTypeBitSet Without(ItemTypeId type)
    {
        if (!Contains(type))
        {
            return this;
        }

        var bytes = (byte[])_bytes.Clone();
        var index = (int)(type.Value >> 3);
        bytes[index] &= (byte)~(1 << (int)(type.Value & 7));
        var length = bytes.Length;
        while (length > 0 && bytes[length - 1] == 0)
        {
            length--;
        }

        return length == 0 ? Empty : new ItemTypeBitSet(bytes[..length]);
    }

    /// <inheritdoc />
    public bool Equals(ItemTypeBitSet? other) => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ItemTypeBitSet);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.AddBytes(_bytes);
        return hash.ToHashCode();
    }
}
