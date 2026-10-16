using Horyzont.Core.World.Blocks;

namespace Horyzont.Core.Crafting;

/// <summary>Properties of the <see cref="StationKind"/> values (K6-32, D5).</summary>
public static class StationKinds
{
    /// <summary>True for stations that process input over time (furnace, refinery).</summary>
    /// <param name="kind">The station kind.</param>
    /// <returns>Whether the station has time.</returns>
    public static bool IsTimed(StationKind kind) => throw new NotImplementedException("K6-32 A2");

    /// <summary>The highest level of the station: 4 for the workbench, 1 for the others.</summary>
    /// <param name="kind">The station kind.</param>
    /// <returns>The highest level.</returns>
    public static byte MaxLevel(StationKind kind) => throw new NotImplementedException("K6-32 A2");
}
