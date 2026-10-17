using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol;

namespace Horyzont.Core.Survival;

/// <summary>The suit modules by item type id, built once from the content and the world registry (K6-34, D10).</summary>
public sealed class SuitModuleTable
{
    private readonly SuitModuleDef?[] _byType = new SuitModuleDef?[ProtocolConstants.MaxItemTypes + 1];

    private SuitModuleTable()
    {
    }

    /// <summary>Builds the table.</summary>
    /// <param name="content">The survival content.</param>
    /// <param name="registry">The item types of the world.</param>
    /// <returns>The table.</returns>
    public static SuitModuleTable Build(SurvivalContent content, ItemTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(registry);
        var table = new SuitModuleTable();
        foreach (var entry in registry.Entries)
        {
            if (entry.Id is not 0 and <= ProtocolConstants.MaxItemTypes && content.Items.TryGet(entry.Key, out var item) && item.SuitModule is { } module)
            {
                table._byType[entry.Id] = module;
            }
        }

        return table;
    }

    /// <summary>Builds the table from a lookup, for a client that has the content of the items but no registry of the world (K6-34 D20). The lookup is called exactly once for every id 1 to <see cref="ProtocolConstants.MaxItemTypes"/>, ascending.</summary>
    /// <param name="lookup">The module statistics of an item type id, null for a type that is no module.</param>
    /// <returns>The table.</returns>
    public static SuitModuleTable Build(Func<ItemTypeId, SuitModuleDef?> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        var table = new SuitModuleTable();
        for (var id = 1; id <= ProtocolConstants.MaxItemTypes; id++)
        {
            table._byType[id] = lookup(new ItemTypeId((ushort)id));
        }

        return table;
    }

    /// <summary>Looks up the module statistics of an item type; allocates nothing.</summary>
    /// <param name="type">The item type.</param>
    /// <param name="module">The statistics when the type is a module.</param>
    /// <returns>True when the type is a module.</returns>
    public bool TryGet(ItemTypeId type, [NotNullWhen(true)] out SuitModuleDef? module)
    {
        module = type.Value <= ProtocolConstants.MaxItemTypes ? _byType[type.Value] : null;
        return module is not null;
    }
}
