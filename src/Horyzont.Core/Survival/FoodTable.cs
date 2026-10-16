using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;

namespace Horyzont.Core.Survival;

/// <summary>The food statistics by item type id, built once from the content and the world registry (K6-33, D8).</summary>
public sealed class FoodTable
{
    private FoodTable()
    {
    }

    /// <summary>Builds the table.</summary>
    /// <param name="content">The survival content.</param>
    /// <param name="registry">The item types of the world.</param>
    /// <returns>The table.</returns>
    public static FoodTable Build(SurvivalContent content, ItemTypeRegistry registry) => throw new NotImplementedException("K6-33 A6");

    /// <summary>Looks up the food statistics of an item type; allocates nothing.</summary>
    /// <param name="type">The item type.</param>
    /// <param name="food">The statistics when the type is food.</param>
    /// <returns>True when the type is food.</returns>
    public bool TryGet(ItemTypeId type, [NotNullWhen(true)] out FoodDef? food) => throw new NotImplementedException("K6-33 A6");
}
