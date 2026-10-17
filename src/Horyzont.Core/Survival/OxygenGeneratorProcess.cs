using Horyzont.Core.Ids;
using Horyzont.Core.Items;

namespace Horyzont.Core.Survival;

/// <summary>
/// The burning of fuel cells by an oxygen generator as pure functions of ticks (K6-34, D12, A1 review 1); the settlement does not depend on how often it is called.
/// A fuel cell is destroyed when it starts to burn, so the input holds only the queue of the cells that have not started yet and taking the last cell out of the input
/// gives no free time. <c>started</c> is the tick the cell that burns now was consumed at.
/// </summary>
public static class OxygenGeneratorProcess
{
    /// <summary>Counts the fuel cells in the input container.</summary>
    /// <param name="input">The slots of the input.</param>
    /// <param name="fuel">The type of the fuel cell.</param>
    /// <returns>The number of cells.</returns>
    public static int CellsIn(ReadOnlySpan<ItemStack> input, ItemTypeId fuel)
    {
        var cells = 0;
        for (var i = 0; i < input.Length; i++)
        {
            if (!input[i].IsEmpty && input[i].Type == fuel)
            {
                cells += input[i].Count;
            }
        }

        return cells;
    }

    /// <summary>
    /// Settles the generator at <paramref name="now"/>. An idle generator (<paramref name="started"/> null) with queued cells consumes one now (burned 1, start now); without cells it stays idle.
    /// A running one has <c>k = (now - started) / ticksPerCell</c> whole periods behind it (0 when <paramref name="now"/> is before the start): it consumes <c>m = min(cells, k)</c> queued cells,
    /// continues from <c>started + m * ticksPerCell</c> and goes idle when <c>k &gt; cells</c> (the cell that burned has ended and no queued cell followed).
    /// </summary>
    /// <remarks>
    /// The function does not know when an item entered the input. The caller must settle <b>before</b> every change of the content of the input (K6-34 review B1, P2-2): taking a cell out without settling first
    /// returns cells that the function already counts as burned (free protection), and putting a cell into an idle generator whose <c>started</c> is long past, without settling first, makes the next
    /// settlement destroy the new cell as if it had burned in the past (lost items).
    /// </remarks>
    /// <param name="cells">Cells waiting in the input (not the one that burns).</param>
    /// <param name="ticksPerCell">Ticks one cell burns.</param>
    /// <param name="started">The tick the current cell was consumed at, null when idle.</param>
    /// <param name="now">The current tick.</param>
    /// <returns>The cells consumed and the next start.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cells"/> is negative or <paramref name="ticksPerCell"/> is zero.</exception>
    public static GeneratorSettlement Settle(int cells, ulong ticksPerCell, Tick? started, Tick now)
    {
        Check(cells, ticksPerCell);
        if (started is not { } start)
        {
            return cells > 0 ? new GeneratorSettlement(1, now) : new GeneratorSettlement(0, null);
        }

        var periods = now.Value >= start.Value ? (now.Value - start.Value) / ticksPerCell : 0UL;
        var consumed = (int)Math.Min((ulong)cells, periods);
        return new GeneratorSettlement(consumed, periods > (ulong)cells ? null : new Tick(start.Value + ((ulong)consumed * ticksPerCell)));
    }

    /// <summary>Whether the generator protects at <paramref name="now"/>: it was started and <c>now &lt; started + (cells + 1) * ticksPerCell</c> (saturating arithmetic); the cell that burns is paid for.</summary>
    /// <param name="cells">Cells waiting in the input.</param>
    /// <param name="ticksPerCell">Ticks one cell burns.</param>
    /// <param name="started">The tick the current cell was consumed at, null when idle.</param>
    /// <param name="now">The current tick.</param>
    /// <returns>True when the generator runs.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cells"/> is negative or <paramref name="ticksPerCell"/> is zero.</exception>
    public static bool IsRunning(int cells, ulong ticksPerCell, Tick? started, Tick now)
    {
        Check(cells, ticksPerCell);
        return started is { } start && now.Value < Deadline(cells, ticksPerCell, start);
    }

    /// <summary>Ticks until the last cell is burned; 0 when the generator does not run.</summary>
    /// <param name="cells">Cells waiting in the input.</param>
    /// <param name="ticksPerCell">Ticks one cell burns.</param>
    /// <param name="started">The tick the current cell was consumed at, null when idle.</param>
    /// <param name="now">The current tick.</param>
    /// <returns>The remaining ticks.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cells"/> is negative or <paramref name="ticksPerCell"/> is zero.</exception>
    public static ulong RemainingTicks(int cells, ulong ticksPerCell, Tick? started, Tick now)
    {
        Check(cells, ticksPerCell);
        if (started is not { } start)
        {
            return 0;
        }

        var deadline = Deadline(cells, ticksPerCell, start);
        return now.Value < deadline ? deadline - now.Value : 0;
    }

    private static void Check(int cells, ulong ticksPerCell)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cells);
        ArgumentOutOfRangeException.ThrowIfZero(ticksPerCell);
    }

    /// <summary>The tick the last cell has burned out at: the start plus one period for the cell that burns and one for each queued cell, saturating.</summary>
    private static ulong Deadline(int cells, ulong ticksPerCell, Tick start)
    {
        var periods = (ulong)cells + 1;
        var length = periods > ulong.MaxValue / ticksPerCell ? ulong.MaxValue : periods * ticksPerCell;
        return length > ulong.MaxValue - start.Value ? ulong.MaxValue : start.Value + length;
    }
}
