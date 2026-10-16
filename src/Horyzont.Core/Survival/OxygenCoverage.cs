using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World;

namespace Horyzont.Core.Survival;

/// <summary>Whether a point is covered by an oxygen generator that runs (K6-34, D12); skeleton of step B1, the logic comes in B4. Public for the plants of K6-38.</summary>
public static class OxygenCoverage
{
    /// <summary>
    /// Whether some generator of <see cref="Horyzont.Core.BlockEntities.BlockEntityStore.Generators"/> on the same planet runs at <paramref name="now"/> and its cell centre is within
    /// <see cref="HazardRules.GeneratorRangeMeters"/> (inclusive) of <paramref name="point"/>. Protection comes from the pure <see cref="OxygenGeneratorProcess.IsRunning"/>, never from the last settlement.
    /// </summary>
    /// <param name="state">The world.</param>
    /// <param name="planet">The planet of the point.</param>
    /// <param name="point">The origin of the player in the frame of the planet (<see cref="ShelterEvaluator.TryGetOrigin"/>).</param>
    /// <param name="fuel">The type of the fuel cell.</param>
    /// <param name="ticksPerCell">Ticks one cell burns.</param>
    /// <param name="now">The current tick.</param>
    /// <param name="rules">The range.</param>
    /// <returns>True when the point is covered.</returns>
    public static bool IsCovered(WorldState state, PlanetId planet, Vector3 point, ItemTypeId fuel, ulong ticksPerCell, Tick now, HazardRules rules) =>
        throw new NotImplementedException("K6-34 B4");
}
