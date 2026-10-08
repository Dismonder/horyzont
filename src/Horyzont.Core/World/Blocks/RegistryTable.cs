using System.Diagnostics.CodeAnalysis;
using System.Text;

using Horyzont.Core.Protocol;

namespace Horyzont.Core.World.Blocks;

/// <summary>Shared implementation of the key ↔ id registries.</summary>
internal sealed class RegistryTable
{
    private readonly Dictionary<string, ushort> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<ushort, string> _byId = [];
    private readonly List<RegistryEntry> _entries = [];
    private readonly int _maxId;
    private readonly string _name;

    internal RegistryTable(string name, int maxId)
    {
        _name = name;
        _maxId = maxId;
    }

    internal IReadOnlyList<RegistryEntry> Entries => _entries;

    internal bool TryGetId(string key, out ushort id) => _byKey.TryGetValue(key, out id);

    internal bool TryGetKey(ushort id, [NotNullWhen(true)] out string? key) => _byId.TryGetValue(id, out key);

    internal bool IsKnown(ushort id) => _byId.ContainsKey(id);

    internal ushort GetOrAssign(string key)
    {
        if (_byKey.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var next = _entries.Count == 0 ? 1 : _entries[^1].Id + 1;
        if (next > _maxId)
        {
            throw new InvalidOperationException($"The {_name} registry is full ({_maxId} entries).");
        }

        Add(new RegistryEntry((ushort)next, key));
        return (ushort)next;
    }

    internal void Add(RegistryEntry entry)
    {
        ValidateKey(entry.Key);
        if (entry.Id == 0 || entry.Id > _maxId)
        {
            throw new InvalidDataException($"The {_name} registry id {entry.Id} is outside 1..{_maxId}.");
        }

        if (_byKey.ContainsKey(entry.Key) || _byId.ContainsKey(entry.Id))
        {
            throw new InvalidDataException($"Duplicate {_name} registry entry {entry.Id} '{entry.Key}'.");
        }

        _byKey.Add(entry.Key, entry.Id);
        _byId.Add(entry.Id, entry.Key);
        var index = _entries.Count;
        while (index > 0 && _entries[index - 1].Id > entry.Id)
        {
            index--;
        }

        _entries.Insert(index, entry);
    }

    private void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key) || Encoding.UTF8.GetByteCount(key) > ProtocolConstants.MaxRegistryKeyBytes)
        {
            throw new InvalidDataException(
                $"The {_name} registry key must have 1..{ProtocolConstants.MaxRegistryKeyBytes} UTF-8 bytes.");
        }
    }
}
