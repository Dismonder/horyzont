using Horyzont.Core.BlockEntities;
using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Survival;
using Horyzont.Core.World;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Builds the settlement of an oxygen generator as one operation (K6-34 D12, the pattern of <see cref="StationSettlements"/>): without a change none, with only a moved start a
/// <c>BlockEntitySetOp</c>, with burned cells <c>CompoundOp[ItemDestroyOp(input, slots ascending, fuel, ItemSink.Fuel) ..., BlockEntitySetOp]</c>. The cells are a function of ticks
/// (<see cref="OxygenGeneratorProcess.Settle"/>), so the result does not depend on how often it is called.
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
    internal static bool TryBuild(WorldState state, HazardContent content, GameRules rules, BlockRef station, Tick now, out Operation operation)
    {
        operation = null!;
        if (!state.BlockEntities.TryGet(station, out var entity) || entity is not OxygenGeneratorBlockState current
            || !state.Constructs.TryGet(station.Construct, out var construct)
            || !state.Containers.TryGet(current.Input, out var input)
            || !state.ItemTypes.TryGetId(content.Generator.Fuel, out var fuel))
        {
            return false;
        }

        var cells = OxygenGeneratorProcess.CellsIn(input.Slots, fuel);
        var settled = OxygenGeneratorProcess.Settle(cells, content.Generator.TicksPerCell(rules.TickRate), current.Started, now);
        if (settled.Burned == 0 && settled.NextStarted == current.Started)
        {
            return false;
        }

        var type = construct.GetBlock(station.Cell).TypeId;
        var next = new BlockEntitySetOp(station.Construct, station.Cell, type, current with { Started = settled.NextStarted });
        if (settled.Burned == 0)
        {
            operation = next;
            return true;
        }

        var slots = input.Slots.ToArray();
        var components = new List<Operation>(slots.Length + 1);
        var left = ItemPlacement.Destroy(input.Id, slots, fuel, settled.Burned, ItemSink.Fuel, components);
        if (left != 0)
        {
            // Settle counts the cells of the same slots, so a difference is a broken invariant (a bug), not a state of the world.
            throw new InvalidOperationException($"The settlement of {station} would burn {settled.Burned} cells but the input holds {settled.Burned - left}.");
        }

        components.Add(next);
        operation = new CompoundOp(components.ToArray());
        return true;
    }
}
