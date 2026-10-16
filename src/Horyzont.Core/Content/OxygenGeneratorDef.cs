namespace Horyzont.Core.Content;

/// <summary>The oxygen generator of the hazard content (K6-34, D8): the fuel it burns and how long a cell lasts.</summary>
/// <param name="Fuel">Item key of the fuel cell, e.g. <c>core:fuel_cell</c>.</param>
/// <param name="SecondsPerCell">Seconds one cell burns, 10 to 86 400.</param>
public sealed record OxygenGeneratorDef(string Fuel, int SecondsPerCell)
{
    /// <summary>
    /// The ticks one fuel cell burns at a tick rate. The only place where seconds become ticks for the settlement of the generator, its coverage and the client, so that the three cannot disagree
    /// (review B1b, P3-6).
    /// </summary>
    /// <param name="tickRate">Ticks per second, at least 1.</param>
    /// <returns>Ticks per cell.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickRate"/> is below 1.</exception>
    public ulong TicksPerCell(int tickRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tickRate, 1);
        return (ulong)SecondsPerCell * (ulong)tickRate;
    }
}
