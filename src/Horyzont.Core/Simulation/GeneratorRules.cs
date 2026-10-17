using Horyzont.Core.BlockEntities;
using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Placing and removing an oxygen generator (K6-34 D12): the compounds <c>[ContainerCreateOp, BlockEntitySetOp, block]</c> and <c>[ContainerRemoveOp, BlockEntitySetOp(null), BlockClearOp]</c>. Called by
/// <see cref="ServerSimulation"/> after <see cref="CraftingRules"/> and before <see cref="FunctionalBlockRules"/>. Reads the content in the resolver only (ZASADY §4); the operations carry the ids and the sizes.
/// </summary>
internal sealed class GeneratorRules(WorldState state, BlockContentTable content, SurvivalContent? survival)
{
    /// <summary>Resolves the intents this class owns: a block with the function of an oxygen generator placed, and a block with the state of one taken down.</summary>
    /// <param name="player">The acting player.</param>
    /// <param name="intent">The validated intent.</param>
    /// <param name="facts">Facts of the validator.</param>
    /// <param name="operation">The operation when <paramref name="reason"/> is <see cref="RejectReason.None"/>.</param>
    /// <param name="reason">The refusal, or none.</param>
    /// <returns>False when the intent does not concern a generator (the caller resolves it otherwise).</returns>
    internal bool TryResolve(PlayerId player, Intent intent, IntentFacts facts, out Operation operation, out RejectReason reason)
    {
        operation = null!;
        reason = RejectReason.None;
        switch (intent)
        {
            case PlaceBlockIntent place:
                var function = content.Get(place.BlockTypeId).Function;
                if (function.Kind != BlockFunctionKind.OxygenGenerator)
                {
                    return false;
                }

                reason = ResolvePlace(player, place, facts, function, out operation);
                return true;

            case RemoveBlockIntent remove:
                if (!state.BlockEntities.TryGet(new BlockRef(remove.Construct, remove.Cell), out var entity) || entity is not OxygenGeneratorBlockState generator)
                {
                    return false;
                }

                reason = ResolveRemove(remove, generator, out operation);
                return true;

            default:
                return false;
        }
    }

    private RejectReason ResolvePlace(PlayerId player, PlaceBlockIntent place, IntentFacts facts, BlockFunction function, out Operation operation)
    {
        operation = null!;
        if (survival?.Hazards is null || function.ContainerSlots is 0 or > ProtocolConstants.MaxContainerSlots)
        {
            return RejectReason.InvalidParameters;
        }

        var block = IntentResolver.Resolve(state, player, place, facts);
        EntityId construct;
        CellPos cell;
        switch (block)
        {
            case BlockSetOp set:
                construct = set.Construct;
                cell = set.Cell;
                break;
            case ConstructCreateOp create:
                construct = create.Construct;
                cell = create.FirstCell;
                break;
            default:
                throw new InvalidOperationException($"A block placement resolved to {block.Kind}.");
        }

        var input = state.ContainerIds.PeekNext();
        operation = new CompoundOp(
        [
            new ContainerCreateOp(input, ContainerKind.StationInput, ContainerHost.ConstructBlock(construct, cell), player, ContainerAccess.Shared, function.ContainerSlots),
            new BlockEntitySetOp(construct, cell, place.BlockTypeId, new OxygenGeneratorBlockState(input, null)),
            block,
        ]);
        return RejectReason.None;
    }

    private RejectReason ResolveRemove(RemoveBlockIntent remove, OxygenGeneratorBlockState generator, out Operation operation)
    {
        operation = null!;
        if (survival?.Hazards is null || !state.Constructs.TryGet(remove.Construct, out var construct) || !state.Containers.TryGet(generator.Input, out var input))
        {
            return RejectReason.InvalidParameters;
        }

        for (var i = 0; i < input.Size; i++)
        {
            if (!input.GetSlot(i).IsEmpty)
            {
                return RejectReason.ContainerNotEmpty;
            }
        }

        operation = new CompoundOp(
        [
            new ContainerRemoveOp(input.Id),
            new BlockEntitySetOp(remove.Construct, remove.Cell, construct.GetBlock(remove.Cell).TypeId, null),
            new BlockClearOp(remove.Construct, remove.Cell),
        ]);
        return RejectReason.None;
    }
}
