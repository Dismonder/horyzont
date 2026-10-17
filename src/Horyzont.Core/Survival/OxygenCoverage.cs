using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;

namespace Horyzont.Core.Survival;

/// <summary>Whether a point is covered by an oxygen generator that runs (K6-34, D12). Public for the plants of K6-38.</summary>
public static class OxygenCoverage
{
    /// <summary>
    /// Whether some generator of <see cref="BlockEntityStore.Generators"/> on the same planet runs at <paramref name="now"/> and its cell centre is within
    /// <see cref="HazardRules.GeneratorRangeMeters"/> (inclusive) of <paramref name="point"/>. Protection comes from the pure <see cref="OxygenGeneratorProcess.IsRunning"/>, never from the last settlement.
    /// </summary>
    /// <param name="state">The world.</param>
    /// <param name="planet">The planet of the point.</param>
    /// <param name="point">The origin of the player in the frame of the planet (<see cref="ShelterEvaluator.TryGetOrigin"/>).</param>
    /// <param name="fuel">The type of the fuel cell.</param>
    /// <param name="ticksPerCell">Ticks one cell burns (<see cref="Horyzont.Core.Content.OxygenGeneratorDef.TicksPerCell"/>).</param>
    /// <param name="now">The current tick.</param>
    /// <param name="rules">The range.</param>
    /// <returns>True when the point is covered.</returns>
    public static bool IsCovered(WorldState state, PlanetId planet, Vector3 point, ItemTypeId fuel, ulong ticksPerCell, Tick now, HazardRules rules)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rules);
        var generators = state.BlockEntities.Generators;
        for (var i = 0; i < generators.Count; i++)
        {
            var block = generators[i];
            // An idle generator (never started, or burned out and settled) cannot run: the cheap tests go before the cell centre and the distance (review B5, P3-5).
            if (!state.BlockEntities.TryGet(block, out var entity) || entity is not OxygenGeneratorBlockState { Started: not null } generator
                || !ContainerReach.TryCellCenter(state, block.Construct, block.Cell, out var centrePlanet, out var centre) || centrePlanet != planet
                || Vector3.Distance(point, centre) > rules.GeneratorRangeMeters
                || !state.Containers.TryGet(generator.Input, out var input))
            {
                continue;
            }

            if (OxygenGeneratorProcess.IsRunning(OxygenGeneratorProcess.CellsIn(input.Slots, fuel), ticksPerCell, generator.Started, now))
            {
                return true;
            }
        }

        return false;
    }
}
