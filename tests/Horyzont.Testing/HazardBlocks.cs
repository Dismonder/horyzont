using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Testing;

/// <summary>
/// The block types of the worlds of <see cref="WorldFactory.CreateWithHazards"/> (K6-34): the six of <see cref="FoodBlocks"/> and the oxygen generator as id 7, a cube with
/// <see cref="BlockFunction.CreateOxygenGenerator"/> and two input slots.
/// </summary>
public static class HazardBlocks
{
    /// <summary>Type id of the oxygen generator.</summary>
    public const ushort GeneratorType = 7;

    /// <summary>Key of the oxygen generator.</summary>
    public const string GeneratorKey = "core:oxygen_generator";

    /// <summary>The block keys of the hazard worlds, in registry order (ids 1 to 7).</summary>
    public static IReadOnlyList<string> BlockKeys { get; } = [.. FoodBlocks.BlockKeys, GeneratorKey];

    /// <summary>The block definitions of <see cref="BlockKeys"/>.</summary>
    /// <param name="generatorSlots">Slots of the input of the generator.</param>
    /// <returns>The definitions by key.</returns>
    public static IReadOnlyDictionary<string, BlockTypeDef> DefinitionsFor(ushort generatorSlots = 2)
    {
        var definitions = new Dictionary<string, BlockTypeDef>(FoodBlocks.DefinitionsFor(), StringComparer.Ordinal)
        {
            [GeneratorKey] = new BlockTypeDef(GeneratorKey, BlockShape.Cube, 1, true, 300f, BlockFunction.CreateOxygenGenerator(generatorSlots)),
        };
        return definitions;
    }

    /// <summary>The content table of a world created by <see cref="WorldFactory.CreateWithHazards"/>.</summary>
    /// <param name="state">The world.</param>
    /// <param name="generatorSlots">Slots of the input of the generator.</param>
    /// <returns>The table.</returns>
    public static BlockContentTable ContentFor(WorldState state, ushort generatorSlots = 2)
    {
        ArgumentNullException.ThrowIfNull(state);
        return BlockContentTable.Create(state.BlockTypes, DefinitionsFor(generatorSlots));
    }
}
