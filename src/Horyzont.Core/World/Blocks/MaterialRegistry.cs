using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Protocol;

namespace Horyzont.Core.World.Blocks;

/// <summary>Terrain materials of a world: text key ↔ number 1..255, 0 is air.</summary>
public sealed class MaterialRegistry
{
    private readonly RegistryTable _table = new("material", ProtocolConstants.MaxMaterials);

    internal MaterialRegistry()
    {
    }

    /// <summary>All entries sorted by id.</summary>
    public IReadOnlyList<RegistryEntry> Entries => _table.Entries;

    /// <summary>Looks up the id of a key.</summary>
    /// <param name="key">Registry key.</param>
    /// <param name="id">The id when found.</param>
    /// <returns>True when the key is registered.</returns>
    public bool TryGetId(string key, out byte id)
    {
        var found = _table.TryGetId(key, out var wide);
        id = (byte)wide;
        return found;
    }

    /// <summary>Looks up the key of an id.</summary>
    /// <param name="id">Material id.</param>
    /// <param name="key">The key when found.</param>
    /// <returns>True when the id is registered.</returns>
    public bool TryGetKey(byte id, [NotNullWhen(true)] out string? key) => _table.TryGetKey(id, out key);

    /// <summary>True when the id is registered (0, air, never is).</summary>
    /// <param name="id">Material id.</param>
    /// <returns>Whether the id is known.</returns>
    public bool IsKnown(byte id) => _table.IsKnown(id);

    /// <summary>Returns the id of a key, assigning the next number to a new key.</summary>
    /// <exception cref="InvalidOperationException">More than <see cref="ProtocolConstants.MaxMaterials"/> materials.</exception>
    internal byte GetOrAssign(string key) => (byte)_table.GetOrAssign(key);

    internal void Add(RegistryEntry entry) => _table.Add(entry);
}
