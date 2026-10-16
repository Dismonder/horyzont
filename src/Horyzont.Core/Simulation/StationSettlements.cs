using Horyzont.Core.BlockEntities;
using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Builds the settlement of a station with time as one operation (K6-32 D16): a compound of the destroys of the input, the creates in the
/// output and the new block state, in that order. Skeleton: the body arrives with step B4.
/// </summary>
internal sealed class StationSettlements(WorldState state, BlockContentTable content, SurvivalContent survival)
{
    /// <summary>Builds the settlement of <paramref name="station"/> at tick <paramref name="now"/>.</summary>
    /// <param name="station">The block with the station state.</param>
    /// <param name="now">The tick to settle at.</param>
    /// <param name="operation">The operation when the method returns true.</param>
    /// <returns>False when nothing changes (no state, no recipe, no piece done and the same start), so no operation is made.</returns>
    internal bool TryBuild(BlockRef station, Tick now, out Operation operation) => throw new NotImplementedException("K6-32 B4");

    /// <summary>The world the settlements read.</summary>
    internal WorldState State => state;

    /// <summary>Block content.</summary>
    internal BlockContentTable Content => content;

    /// <summary>Survival content.</summary>
    internal SurvivalContent Survival => survival;
}
