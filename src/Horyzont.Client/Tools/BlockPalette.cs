using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Client.Tools;

/// <summary>Block types the player can build with: the registry of the server hello in <c>Id</c> order, without the fallback block.</summary>
public sealed class BlockPalette
{
    private readonly RegistryEntry[] _entries;

    /// <summary>Initializes a new instance of the <see cref="BlockPalette"/> class.</summary>
    /// <param name="registry">Block type registry.</param>
    public BlockPalette(IReadOnlyList<RegistryEntry> registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _entries = [.. registry.Where(e => e.Key != BlockTypeRegistry.MissingKey).OrderBy(e => e.Id)];
    }

    /// <summary>Gets the palette entries in <c>Id</c> order.</summary>
    public IReadOnlyList<RegistryEntry> Entries => _entries;

    /// <summary>Gets the number of entries.</summary>
    public int Count => _entries.Length;

    /// <summary>Builds the palette of a server hello.</summary>
    /// <param name="hello">The hello.</param>
    /// <returns>The palette.</returns>
    public static BlockPalette FromHello(ServerHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        return new BlockPalette(hello.BlockTypes);
    }

    /// <summary>The block type id <paramref name="steps"/> entries after <paramref name="current"/>, wrapping around.</summary>
    /// <param name="current">Current id; not in the palette starts from the first entry.</param>
    /// <param name="steps">Signed number of steps.</param>
    /// <returns>The id, or 0 for an empty palette.</returns>
    public ushort Step(ushort current, int steps)
    {
        if (_entries.Length == 0)
        {
            return 0;
        }

        var index = IndexOf(_entries, current);
        return _entries[Wrap((index < 0 ? 0 : index) + steps, _entries.Length)].Id;
    }

    /// <summary>The first id of the palette, or 0 when empty.</summary>
    /// <returns>The id.</returns>
    public ushort First() => _entries.Length == 0 ? (ushort)0 : _entries[0].Id;

    internal static int Wrap(int index, int count) => (((index % count) + count) % count);

    internal static int IndexOf(RegistryEntry[] entries, int id)
    {
        for (var i = 0; i < entries.Length; i++)
        {
            if (entries[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}
