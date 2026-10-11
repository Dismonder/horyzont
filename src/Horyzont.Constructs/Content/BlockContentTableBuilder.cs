using Horyzont.Core.World.Blocks;

namespace Horyzont.Constructs.Content;

/// <summary>Builds the Core <see cref="BlockContentTable"/> from the parsed content and the registry of a world.</summary>
public static class BlockContentTableBuilder
{
    /// <summary>Builds the table: registry keys unknown to the content become <c>core:missing</c>.</summary>
    /// <param name="catalog">Parsed block content.</param>
    /// <param name="registry">Block types of the world.</param>
    /// <returns>The table, identical for the server and for clients with the same content.</returns>
    public static BlockContentTable Build(BlockTypeCatalog catalog, BlockTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(registry);
        var definitions = new Dictionary<string, BlockTypeDef>(StringComparer.Ordinal);
        foreach (var block in catalog.Blocks)
        {
            definitions.Add(block.Def.Key, block.Def);
        }

        return BlockContentTable.Create(registry, definitions);
    }
}
