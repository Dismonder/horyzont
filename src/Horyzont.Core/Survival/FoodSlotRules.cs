using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Progress;

namespace Horyzont.Core.Survival;

/// <summary>Where newly eaten food goes (K6-33, D11); a pure function shared by the server and the client.</summary>
public static class FoodSlotRules
{
    /// <summary>Reject reason 49 of the plan; the enum member arrives in step B2.</summary>
    private const RejectReason FoodAlreadyActive = (RejectReason)49;

    /// <summary>Places food in the slots.</summary>
    /// <param name="current">The current slots.</param>
    /// <param name="type">The eaten item type.</param>
    /// <param name="table">The food statistics.</param>
    /// <param name="now">The current tick, stored as the tick of the new slot.</param>
    /// <param name="rules">The rules.</param>
    /// <param name="tickRate">Ticks per second.</param>
    /// <param name="next">The slots after eating when the result is <see cref="RejectReason.None"/>; expired slots other than the one used are emptied.</param>
    /// <returns><see cref="RejectReason.None"/>, or code 49 (food already active) when the same food has more than the repeat fraction left or no slot is free.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickRate"/> is not positive.</exception>
    public static RejectReason TryPlace(in FoodSlots current, ItemTypeId type, FoodTable table, Tick now, VitalsRules rules, int tickRate, out FoodSlots next)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickRate);
        next = default;
        if (!table.TryGet(type, out _))
        {
            return RejectReason.InvalidParameters;
        }

        // A free slot is empty or holds food that is gone (also food whose type has no statistics any more); the same type in a slot is the one to replace
        // when at most the repeat fraction is left.
        var target = -1;
        var firstFree = -1;
        Span<bool> free = stackalloc bool[FoodSlots.Count];
        for (var i = 0; i < FoodSlots.Count; i++)
        {
            var slot = current[i];
            double remaining = 0.0;
            if (!slot.IsEmpty && table.TryGet(slot.Type, out var food))
            {
                remaining = FoodCurve.Remaining(slot.Eaten, now, food.DurationSeconds * (ulong)tickRate);
            }

            free[i] = remaining <= 0.0;
            if (free[i] && firstFree < 0)
            {
                firstFree = i;
            }

            if (!slot.IsEmpty && slot.Type == type)
            {
                if (remaining > rules.FoodRepeatFraction)
                {
                    return FoodAlreadyActive;
                }

                target = i;
            }
        }

        if (target < 0)
        {
            target = firstFree;
        }

        if (target < 0)
        {
            return FoodAlreadyActive;
        }

        // The new food takes its slot; the other slots that are gone become empty, the active ones keep their index and tick.
        var kept0 = free[0] ? default : current.Slot0;
        var kept1 = free[1] ? default : current.Slot1;
        var kept2 = free[2] ? default : current.Slot2;
        var placed = new FoodSlot(type, now);
        next = new FoodSlots(target == 0 ? placed : kept0, target == 1 ? placed : kept1, target == 2 ? placed : kept2);
        return RejectReason.None;
    }
}
