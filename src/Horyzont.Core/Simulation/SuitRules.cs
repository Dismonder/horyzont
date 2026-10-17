using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Survival;
using Horyzont.Core.World;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Resolution of <see cref="SuitRefillIntent"/> into the compound <c>ItemDestroyOp(slot, type, 1, SuitRefill)</c> then <c>SuitRefillOp</c> (K6-34 D7). The container and the slot were checked by
/// <see cref="SurvivalRules.CheckItemParameters"/>. Reads the content (the refill, the modules) and the vitals of the player (outside the journal) only to decide a refusal; the operation carries every number
/// the applier needs (the amount and the capacity).
/// </summary>
/// <param name="state">The world.</param>
/// <param name="content">The survival content.</param>
/// <param name="rules">The hazard rules of the simulation: the one source of the base capacity, shared with the tracker of the server.</param>
internal sealed class SuitRules(WorldState state, SurvivalContent content, HazardRules rules)
{
    private readonly SuitModuleTable _modules = SuitModuleTable.Build(content, state.ItemTypes);

    /// <summary>Resolves a validated intent; the checks of D7 after the parameter step, in their order.</summary>
    /// <param name="player">The acting player.</param>
    /// <param name="intent">The intent.</param>
    /// <param name="operation">The compound when the method returns <see cref="RejectReason.None"/>.</param>
    /// <returns>
    /// <see cref="RejectReason.NotEnoughItems"/> for an empty slot, <see cref="RejectReason.InvalidParameters"/> for an item that refills nothing and for a resource that is already full
    /// (the value is at or above the capacity; a player without vitals or without a suit holds the capacity), else none.
    /// </returns>
    internal RejectReason Resolve(PlayerId player, SuitRefillIntent intent, out Operation operation)
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

        if (!state.ItemTypes.TryGetKey(stack.Type, out var key) || !content.Items.TryGet(key, out var def) || def.SuitRefill is not { } refill)
        {
            return RejectReason.InvalidParameters;
        }

        var equipment = state.Containers.TryGetOfPlayer(player, ContainerKind.Equipment, out var worn) ? worn.Slots : default;
        var capacity = SuitLoadout.Compute(equipment, _modules, rules).Capacity[refill.Resource];
        var current = state.Players.TryGet(player, out var playerState) && playerState.Vitals?.Suit is { } suit ? suit[refill.Resource] : capacity;
        if (current >= capacity)
        {
            return RejectReason.InvalidParameters;
        }

        operation = new CompoundOp(
        [
            new ItemDestroyOp(new SlotRef(intent.Container, intent.Slot), stack.Type, 1, ItemSink.SuitRefill),
            new SuitRefillOp(player, refill.Resource, refill.Amount, (ushort)capacity),
        ]);
        return RejectReason.None;
    }
}
