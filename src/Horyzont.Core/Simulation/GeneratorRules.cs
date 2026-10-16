using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Placing and removing an oxygen generator (K6-34 D12): the compounds <c>[ContainerCreateOp, BlockEntitySetOp, block]</c> and <c>[ContainerRemoveOp, BlockEntitySetOp(null), BlockClearOp]</c>; skeleton of step B1,
/// the logic and the place in the resolver chain (after <c>CraftingRules</c>, before <c>FunctionalBlockRules</c>) come in B4.
/// </summary>
internal static class GeneratorRules
{
    /// <summary>Resolves a place or remove intent that concerns a generator.</summary>
    /// <param name="state">The world.</param>
    /// <param name="content">The survival content (hazards).</param>
    /// <param name="player">The acting player.</param>
    /// <param name="intent">The intent.</param>
    /// <param name="operation">The operation when the method returns true and the reason is none.</param>
    /// <param name="reason">The reason of a refusal.</param>
    /// <returns>True when the intent concerns a generator and was resolved here.</returns>
    internal static bool TryResolve(WorldState state, SurvivalContent? content, PlayerId player, Intent intent, out Operation operation, out RejectReason reason) =>
        throw new NotImplementedException("K6-34 B4");
}
