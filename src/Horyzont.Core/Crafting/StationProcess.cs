using Horyzont.Core.Ids;
using Horyzont.Core.Items;

namespace Horyzont.Core.Crafting;

/// <summary>The settlement of a station with time as a pure function of ticks (K6-32, D15); shared by the server and the client.</summary>
public static class StationProcess
{
    /// <summary>
    /// Settles the pieces finished between <paramref name="started"/> and <paramref name="now"/>; allocates nothing. The pieces that can be made are the
    /// least of what the input holds (ingredients and fuel) and what the output has room for; the finished ones are at most the whole durations elapsed since
    /// the start. The next start moves by the finished pieces, so the result does not depend on when and how often the station is settled.
    /// </summary>
    /// <param name="input">Slots of the input container.</param>
    /// <param name="output">Slots of the output container.</param>
    /// <param name="recipe">The recipe.</param>
    /// <param name="durationTicks">Ticks per piece, at least 1.</param>
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
        ushort resultStackLimit)
    {
        ArgumentOutOfRangeException.ThrowIfZero(durationTicks);
        var possible = Possible(input, output, recipe, resultStackLimit);
        if (started is not { } start)
        {
            return new StationSettlement(0, possible >= 1 ? now : null);
        }

        var elapsed = now.Value > start.Value ? now.Value - start.Value : 0UL;
        var whole = elapsed / durationTicks;
        var done = whole < (ulong)possible ? (int)whole : possible;
        var remaining = possible - done;
        return new StationSettlement(done, remaining >= 1 ? new Tick(start.Value + ((ulong)done * durationTicks)) : null);
    }

    /// <summary>Progress of the current piece for a progress bar: the part of the duration elapsed since the last finished piece.</summary>
    /// <param name="started">Tick the current piece started.</param>
    /// <param name="now">Current tick; a tick before the start (an old snapshot) gives 0.</param>
    /// <param name="durationTicks">Ticks per piece, at least 1.</param>
    /// <returns>A value in [0, 1).</returns>
    public static double Progress(Tick started, Tick now, ulong durationTicks)
    {
        ArgumentOutOfRangeException.ThrowIfZero(durationTicks);
        return now.Value <= started.Value ? 0.0 : (double)((now.Value - started.Value) % durationTicks) / durationTicks;
    }

    /// <summary>How many pieces the input and the room in the output allow, counted on the slots as they are.</summary>
    private static int Possible(ReadOnlySpan<ItemStack> input, ReadOnlySpan<ItemStack> output, in StationRecipe recipe, ushort resultStackLimit)
    {
        var result = recipe.Result;
        var possible = result.Count == 0 ? int.MaxValue : Room(output, result.Type, resultStackLimit) / result.Count;
        var ingredients = recipe.Ingredients;
        var fuel = recipe.Fuel;
        for (var i = 0; i < ingredients.Count && possible > 0; i++)
        {
            possible = Math.Min(possible, PiecesOf(input, ingredients[i].Type, NeedOf(ingredients, fuel, ingredients[i].Type)));
        }

        for (var i = 0; i < fuel.Count && possible > 0; i++)
        {
            possible = Math.Min(possible, PiecesOf(input, fuel[i].Type, NeedOf(ingredients, fuel, fuel[i].Type)));
        }

        return possible;
    }

    /// <summary>What one piece needs of a type, over the ingredients and the fuel together.</summary>
    private static int NeedOf(IReadOnlyList<StationEntry> ingredients, IReadOnlyList<StationEntry> fuel, ItemTypeId type)
    {
        var need = 0;
        for (var i = 0; i < ingredients.Count; i++)
        {
            need += ingredients[i].Type == type ? ingredients[i].Count : 0;
        }

        for (var i = 0; i < fuel.Count; i++)
        {
            need += fuel[i].Type == type ? fuel[i].Count : 0;
        }

        return need;
    }

    private static int PiecesOf(ReadOnlySpan<ItemStack> slots, ItemTypeId type, int need)
    {
        if (need == 0)
        {
            return int.MaxValue;
        }

        var have = 0;
        for (var i = 0; i < slots.Length; i++)
        {
            have += slots[i].Type == type ? slots[i].Count : 0;
        }

        return have / need;
    }

    /// <summary>The items of a type the slots can still take: free space of stacks of the type up to the limit, and whole empty slots.</summary>
    private static int Room(ReadOnlySpan<ItemStack> slots, ItemTypeId type, ushort limit)
    {
        var room = 0;
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].IsEmpty)
            {
                room += limit;
            }
            else if (slots[i].Type == type && slots[i].Count < limit)
            {
                room += limit - slots[i].Count;
            }
        }

        return room;
    }
}
