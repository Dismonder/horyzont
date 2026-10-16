using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Survival;
using Horyzont.Core.World;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Resolution of <see cref="EatIntent"/> into the compound of eating (K6-33 D10, D11): <c>ItemDestroyOp(slot, type, 1, Eat)</c> then <c>FoodSetOp</c>,
/// so an eaten item never vanishes without an effect and an effect never comes without an item. Reads the food statistics (the content) and the current
/// tick; the operation it builds carries every value the applier needs (ZASADY 4). The container and the slot were checked by the validator.
/// </summary>
/// <param name="state">The world.</param>
/// <param name="content">The survival content.</param>
/// <param name="rules">The game rules (ticks per second).</param>
/// <param name="vitals">The vitals rules of the world: the one source for the repeat fraction and the curve, shared with the vitals service of the server.</param>
internal sealed class FoodRules(WorldState state, SurvivalContent content, GameRules rules, VitalsRules vitals)
{
    private readonly int _tickRate = rules.TickRate > 0 ? rules.TickRate : throw new ArgumentOutOfRangeException(nameof(rules), "The tick rate must be positive.");
    private readonly FoodTable _table = FoodTable.Build(content, state.ItemTypes);

    /// <summary>Resolves a validated eat intent.</summary>
    /// <param name="player">The eating player.</param>
    /// <param name="intent">The intent.</param>
    /// <param name="operation">The compound when the result is <see cref="RejectReason.None"/>.</param>
    /// <returns>
    /// <see cref="RejectReason.NotEnoughItems"/> for an empty slot, <see cref="RejectReason.InvalidParameters"/> for an item that is not food,
    /// <see cref="RejectReason.FoodAlreadyActive"/> when <see cref="FoodSlotRules.TryPlace"/> refuses, else none.
    /// </returns>
    internal RejectReason Resolve(PlayerId player, EatIntent intent, out Operation operation)
    {
        operation = null!;
        if (!state.Containers.TryGet(intent.Container, out var container) || intent.Slot >= container.Size)
        {
            return RejectReason.InvalidParameters;
        }

        var stack = container.GetSlot(intent.Slot);
        if (stack.IsEmpty)
        {
            return RejectReason.NotEnoughItems;
        }

        if (!_table.TryGet(stack.Type, out _))
        {
            return RejectReason.InvalidParameters;
        }

        var current = state.Progress.TryGet(player, out var progress) ? progress.Food : FoodSlots.Empty;
        var reason = FoodSlotRules.TryPlace(current, stack.Type, _table, state.CurrentTick, vitals, _tickRate, out var next);
        if (reason != RejectReason.None)
        {
            return reason;
        }

        operation = new CompoundOp([new ItemDestroyOp(new SlotRef(intent.Container, intent.Slot), stack.Type, 1, ItemSink.Eat), new FoodSetOp(player, next)]);
        return RejectReason.None;
    }
}
