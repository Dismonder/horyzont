using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Builds the settlement of an oxygen generator as one operation (K6-34 D12, the pattern of <see cref="StationSettlements"/>): without a change none, with only a moved start a
/// <c>BlockEntitySetOp</c>, with burned cells <c>CompoundOp[ItemDestroyOp(input, slots ascending, fuel, ItemSink.Fuel) ..., BlockEntitySetOp]</c>; skeleton of step B1, the logic comes in B4.
/// </summary>
internal static class GeneratorSettlements
{
    /// <summary>Builds the settlement of the generator at <paramref name="station"/> at tick <paramref name="now"/>.</summary>
    /// <param name="state">The world.</param>
    /// <param name="content">The hazard content (fuel and seconds per cell).</param>
    /// <param name="rules">The game rules (tick rate).</param>
    /// <param name="station">The block with the generator state.</param>
    /// <param name="now">The tick to settle at.</param>
    /// <param name="operation">The operation when the method returns true.</param>
    /// <returns>False when nothing changes.</returns>
    internal static bool TryBuild(WorldState state, HazardContent content, GameRules rules, Horyzont.Core.BlockEntities.BlockRef station, Tick now, out Operation operation) =>
        throw new NotImplementedException("K6-34 B4");
}
