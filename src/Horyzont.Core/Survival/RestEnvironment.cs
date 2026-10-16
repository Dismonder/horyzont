using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Survival;

/// <summary>The roof, the heat and the comfort around a point (K6-33 D14). The logic arrives with step B4.</summary>
public static class RestEnvironment
{
    /// <summary>Evaluates the surroundings of a point; called every 30 ticks for the resting players on the main thread, never by the applier.</summary>
    /// <param name="state">The world (constructs).</param>
    /// <param name="blocks">Block content: comfort and heat.</param>
    /// <param name="terrain">The terrain (the roof ray).</param>
    /// <param name="planet">Planet of the point.</param>
    /// <param name="point">The point in the frame of the planet.</param>
    /// <param name="rules">The numbers of the ranges.</param>
    /// <returns>The conditions.</returns>
    public static RestConditions Evaluate(
        WorldState state, BlockContentTable blocks, ITerrainService terrain, PlanetId planet, Vector3 point, VitalsRules rules) =>
        throw new NotImplementedException("K6-33 B4");
}
