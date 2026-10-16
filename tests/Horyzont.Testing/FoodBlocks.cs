using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Testing;

/// <summary>
/// The block types of the worlds of <see cref="WorldFactory.CreateWithFood"/> (K6-33): the five of <see cref="FunctionalBlocks"/> and a campfire. The bed
/// and the lamp give 1 comfort each, the campfire gives 1 comfort and heat (D9), so a bed, a lamp and a campfire together make comfort 3.
/// </summary>
public static class FoodBlocks
{
    /// <summary>Type id of the campfire.</summary>
    public const ushort CampfireType = 6;

    /// <summary>Key of the campfire.</summary>
    public const string CampfireKey = "core:campfire";

    /// <summary>The block keys of the food worlds, in registry order (ids 1 to 6).</summary>
    public static IReadOnlyList<string> BlockKeys { get; } = [.. FunctionalBlocks.BlockKeys, CampfireKey];

    /// <summary>The block definitions of <see cref="BlockKeys"/>.</summary>
    /// <param name="chestSlots">Slots of the container of a chest.</param>
    /// <returns>The definitions by key.</returns>
    public static IReadOnlyDictionary<string, BlockTypeDef> DefinitionsFor(ushort chestSlots = 4)
    {
        var definitions = new Dictionary<string, BlockTypeDef>(FunctionalBlocks.DefinitionsFor(chestSlots), StringComparer.Ordinal);
        definitions[FunctionalBlocks.BedKey] = definitions[FunctionalBlocks.BedKey] with { Comfort = 1 };
        definitions[FunctionalBlocks.LampKey] = definitions[FunctionalBlocks.LampKey] with { Comfort = 1 };
        definitions.Add(CampfireKey, new BlockTypeDef(CampfireKey, BlockShape.Cube, 1, true, 50f, BlockFunction.None, Comfort: 1, Heat: true));
        return definitions;
    }

    /// <summary>The content table of a world created by <see cref="WorldFactory.CreateWithFood"/>.</summary>
    /// <param name="state">The world.</param>
    /// <returns>The table.</returns>
    public static BlockContentTable ContentFor(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return BlockContentTable.Create(state.BlockTypes, DefinitionsFor());
    }
}
