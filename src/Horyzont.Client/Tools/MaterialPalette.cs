using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Client.Tools;

/// <summary>Materials the player can fill the terrain with: the registry of the server hello in <c>Id</c> order, without excluded keys.</summary>
public sealed class MaterialPalette
{
    /// <summary>Key of the indestructible core material of the game (K2-08), excluded by default (open question P4).</summary>
    public const string CoreMaterialKey = "core:bedrock";

    private readonly RegistryEntry[] _entries;

    /// <summary>Initializes a new instance of the <see cref="MaterialPalette"/> class.</summary>
    /// <param name="registry">Material registry.</param>
    /// <param name="excludedKeys">Keys left out; <c>null</c> excludes <see cref="CoreMaterialKey"/>.</param>
    public MaterialPalette(IReadOnlyList<RegistryEntry> registry, IReadOnlyCollection<string>? excludedKeys = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var excluded = excludedKeys ?? [CoreMaterialKey];
        _entries = [.. registry.Where(e => e.Id is > 0 and <= byte.MaxValue && !excluded.Contains(e.Key)).OrderBy(e => e.Id)];
    }

    /// <summary>Gets the palette entries in <c>Id</c> order.</summary>
    public IReadOnlyList<RegistryEntry> Entries => _entries;

    /// <summary>Gets the number of entries.</summary>
    public int Count => _entries.Length;

    /// <summary>Builds the palette of a server hello.</summary>
    /// <param name="hello">The hello.</param>
    /// <param name="excludedKeys">Keys left out; <c>null</c> excludes <see cref="CoreMaterialKey"/>.</param>
    /// <returns>The palette.</returns>
    public static MaterialPalette FromHello(ServerHello hello, IReadOnlyCollection<string>? excludedKeys = null)
    {
        ArgumentNullException.ThrowIfNull(hello);
        return new MaterialPalette(hello.Materials, excludedKeys);
    }

    /// <summary>The material id <paramref name="steps"/> entries after <paramref name="current"/>, wrapping around.</summary>
    /// <param name="current">Current id; not in the palette starts from the first entry.</param>
    /// <param name="steps">Signed number of steps.</param>
    /// <returns>The id, or 0 for an empty palette.</returns>
    public byte Step(byte current, int steps)
    {
        if (_entries.Length == 0)
        {
            return 0;
        }

        var index = BlockPalette.IndexOf(_entries, current);
        return (byte)_entries[BlockPalette.Wrap((index < 0 ? 0 : index) + steps, _entries.Length)].Id;
    }

    /// <summary>The first id of the palette, or 0 when empty.</summary>
    /// <returns>The id.</returns>
    public byte First() => _entries.Length == 0 ? (byte)0 : (byte)_entries[0].Id;
}
