using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Crafting;

/// <summary>The level of a workbench from the upgrades around it (K6-32, D14). Skeleton: the body arrives with step B4.</summary>
public static class StationLevels
{
    /// <summary>
    /// 1 plus the number of different block types with the function <see cref="BlockFunctionKind.StationUpgrade"/> of the same station kind whose cell
    /// centre lies within <c>GameRules.StationUpgradeRangeMeters</c> of the centre of the station cell, in any construct on the same planet,
    /// capped at <see cref="StationKinds.MaxLevel"/>.
    /// </summary>
    /// <param name="state">World state.</param>
    /// <param name="content">Block content.</param>
    /// <param name="rules">Game rules (<c>StationUpgradeRangeMeters</c>).</param>
    /// <param name="construct">Construct of the station.</param>
    /// <param name="cell">Cell of the station.</param>
    /// <returns>The level, at least 1.</returns>
    public static int Compute(WorldState state, GameRules rules, BlockContentTable content, EntityId construct, CellPos cell) =>
        throw new NotImplementedException("K6-32 B4");
}
