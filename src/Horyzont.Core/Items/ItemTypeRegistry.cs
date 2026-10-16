using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Core.Items;

/// <summary>
/// Item types of a world (<c>item_types</c>): text key ↔ <see cref="ItemTypeId"/> 1..<see cref="ProtocolConstants.MaxItemTypes"/>.
/// Grows only when a world is created or loaded with newer content, never through operations.
/// </summary>
public sealed class ItemTypeRegistry : IItemTypeKeys
{
    private readonly RegistryTable _table = new("item type", ProtocolConstants.MaxItemTypes);

    internal ItemTypeRegistry()
    {
    }

    /// <summary>All entries sorted by id.</summary>
    public IReadOnlyList<RegistryEntry> Entries => _table.Entries;

    /// <summary>Looks up the id of a key.</summary>
    /// <param name="key">Content key.</param>
    /// <param name="id">The id when found.</param>
    /// <returns>True when the key is registered.</returns>
    public bool TryGetId(string key, out ItemTypeId id)
    {
        var found = _table.TryGetId(key, out var raw);
        id = new ItemTypeId(raw);
        return found;
    }

    /// <summary>Looks up the key of an id.</summary>
    /// <param name="id">Item type id.</param>
    /// <param name="key">The key when found.</param>
    /// <returns>True when the id is registered.</returns>
    public bool TryGetKey(ItemTypeId id, [NotNullWhen(true)] out string? key) => _table.TryGetKey(id.Value, out key);

    /// <summary>True when the id is registered (<see cref="ItemTypeId.None"/> never is).</summary>
    /// <param name="id">Item type id.</param>
    /// <returns>Whether the id is known.</returns>
    public bool IsKnown(ItemTypeId id) => _table.IsKnown(id.Value);

    internal ItemTypeId GetOrAssign(string key) => new(_table.GetOrAssign(key));

    internal void Add(RegistryEntry entry) => _table.Add(entry);
}
