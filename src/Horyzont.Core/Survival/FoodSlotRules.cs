using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Progress;

namespace Horyzont.Core.Survival;

/// <summary>Where newly eaten food goes (K6-33, D11); a pure function shared by the server and the client.</summary>
public static class FoodSlotRules
{
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
    public static RejectReason TryPlace(in FoodSlots current, ItemTypeId type, FoodTable table, Tick now, VitalsRules rules, int tickRate, out FoodSlots next) =>
        throw new NotImplementedException("K6-33 A6");
}
