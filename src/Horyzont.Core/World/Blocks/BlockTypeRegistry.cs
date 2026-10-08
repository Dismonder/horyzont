using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Protocol;

namespace Horyzont.Core.World.Blocks;

/// <summary>Block types of a world: text key ↔ number, numbers from 1, 0 is the empty block.</summary>
public sealed class BlockTypeRegistry
{
    /// <summary>Placeholder type for keys unknown to the game content when a world is loaded.</summary>
    public const string MissingKey = "core:missing";

    private readonly RegistryTable _table = new("block type", ProtocolConstants.MaxBlockTypes);

    internal BlockTypeRegistry()
    {
    }

    /// <summary>All entries sorted by id.</summary>
    public IReadOnlyList<RegistryEntry> Entries => _table.Entries;

    /// <summary>Looks up the id of a key.</summary>
    /// <param name="key">Registry key.</param>
    /// <param name="id">The id when found.</param>
    /// <returns>True when the key is registered.</returns>
    public bool TryGetId(string key, out ushort id) => _table.TryGetId(key, out id);

    /// <summary>Looks up the key of an id.</summary>
    /// <param name="id">Block type id.</param>
    /// <param name="key">The key when found.</param>
    /// <returns>True when the id is registered.</returns>
    public bool TryGetKey(ushort id, [NotNullWhen(true)] out string? key) => _table.TryGetKey(id, out key);

    /// <summary>True when the id is registered (0, the empty block, never is).</summary>
    /// <param name="id">Block type id.</param>
    /// <returns>Whether the id is known.</returns>
    public bool IsKnown(ushort id) => _table.IsKnown(id);

    /// <summary>Returns the id of a key, assigning the next number to a new key.</summary>
    /// <exception cref="InvalidOperationException">More than <see cref="ProtocolConstants.MaxBlockTypes"/> types.</exception>
    internal ushort GetOrAssign(string key) => _table.GetOrAssign(key);

    internal void Add(RegistryEntry entry) => _table.Add(entry);
}
