using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;

namespace Horyzont.Core.Survival;

/// <summary>The suit modules by item type id, built once from the content and the world registry (K6-34, D10).</summary>
public sealed class SuitModuleTable
{
    private SuitModuleTable()
    {
    }

    /// <summary>Builds the table.</summary>
    /// <param name="content">The survival content.</param>
    /// <param name="registry">The item types of the world.</param>
    /// <returns>The table.</returns>
    public static SuitModuleTable Build(SurvivalContent content, ItemTypeRegistry registry) => throw new NotImplementedException("K6-34 A6");

    /// <summary>Looks up the module statistics of an item type; allocates nothing.</summary>
    /// <param name="type">The item type.</param>
    /// <param name="module">The statistics when the type is a module.</param>
    /// <returns>True when the type is a module.</returns>
    public bool TryGet(ItemTypeId type, [NotNullWhen(true)] out SuitModuleDef? module) => throw new NotImplementedException("K6-34 A6");
}
