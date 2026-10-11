using Horyzont.Constructs.Content;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Blocks;
using Horyzont.Game.Planet.Generators;

namespace Horyzont.Game.Boot;

public sealed partial class GameComposition
{
    /// <summary>
    /// The server rules for the ship mode: the highest terrain the planet generator can produce, so Core can skip ship blocks that
    /// cannot reach the ground when it looks for blocks embedded in terrain (K5-23).
    /// </summary>
    /// <param name="rules">The rules.</param>
    /// <returns>The rules with <see cref="GameRules.TerrainMaxReliefMeters"/> set from the generator.</returns>
    internal static GameRules WithShipRules(GameRules rules) => rules with { TerrainMaxReliefMeters = PlanetGeneratorV1.MaxReliefMeters };

    /// <summary>The block definitions (mass and function) the server simulation builds its content table from.</summary>
    /// <param name="catalog">The loaded block content.</param>
    /// <returns>Definitions by block key, <c>core:missing</c> included.</returns>
    internal static IReadOnlyDictionary<string, BlockTypeDef> ShipBlockDefinitions(BlockTypeCatalog catalog)
    {
        var definitions = new Dictionary<string, BlockTypeDef>(StringComparer.Ordinal);
        foreach (var block in catalog.Blocks)
        {
            definitions.Add(block.Def.Key, block.Def);
        }

        return definitions;
    }
}
