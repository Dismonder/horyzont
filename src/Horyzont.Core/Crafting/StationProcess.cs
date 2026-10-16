using Horyzont.Core.Ids;
using Horyzont.Core.Items;

namespace Horyzont.Core.Crafting;

/// <summary>The settlement of a station with time as a pure function of ticks (K6-32, D15); shared by the server and the client.</summary>
public static class StationProcess
{
    /// <summary>Settles the pieces finished between <paramref name="started"/> and <paramref name="now"/>; allocates nothing.</summary>
    /// <param name="input">Slots of the input container.</param>
    /// <param name="output">Slots of the output container.</param>
    /// <param name="recipe">The recipe.</param>
    /// <param name="durationTicks">Ticks per piece.</param>
    /// <param name="started">Tick the current piece started, null when idle.</param>
    /// <param name="now">Current tick.</param>
    /// <param name="resultStackLimit">Stack limit of the result item.</param>
    /// <returns>The finished pieces and the start of the next one.</returns>
    public static StationSettlement Settle(
        ReadOnlySpan<ItemStack> input,
        ReadOnlySpan<ItemStack> output,
        in StationRecipe recipe,
        ulong durationTicks,
        Tick? started,
        Tick now,
        ushort resultStackLimit) => throw new NotImplementedException("K6-32 A6");

    /// <summary>Progress of the current piece for a progress bar: the part of the duration elapsed since the last finished piece.</summary>
    /// <param name="started">Tick the current piece started.</param>
    /// <param name="now">Current tick.</param>
    /// <param name="durationTicks">Ticks per piece.</param>
    /// <returns>A value in [0, 1).</returns>
    public static double Progress(Tick started, Tick now, ulong durationTicks) => throw new NotImplementedException("K6-32 A6");
}
