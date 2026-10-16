using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol;

namespace Horyzont.Core.Survival;

/// <summary>The food statistics by item type id, built once from the content and the world registry (K6-33, D8).</summary>
public sealed class FoodTable
{
    private readonly FoodDef?[] _byType = new FoodDef?[ProtocolConstants.MaxItemTypes + 1];

    private FoodTable()
    {
    }

    /// <summary>Builds the table.</summary>
    /// <param name="content">The survival content.</param>
    /// <param name="registry">The item types of the world.</param>
    /// <returns>The table.</returns>
    public static FoodTable Build(SurvivalContent content, ItemTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(registry);
        var table = new FoodTable();
        foreach (var entry in registry.Entries)
        {
            if (entry.Id is not 0 and <= ProtocolConstants.MaxItemTypes && content.Items.TryGet(entry.Key, out var item) && item.Food is { } food)
            {
                table._byType[entry.Id] = food;
            }
        }

        return table;
    }

    /// <summary>Looks up the food statistics of an item type; allocates nothing.</summary>
    /// <param name="type">The item type.</param>
    /// <param name="food">The statistics when the type is food.</param>
    /// <returns>True when the type is food.</returns>
    public bool TryGet(ItemTypeId type, [NotNullWhen(true)] out FoodDef? food)
    {
        food = type.Value <= ProtocolConstants.MaxItemTypes ? _byType[type.Value] : null;
        return food is not null;
    }
}
