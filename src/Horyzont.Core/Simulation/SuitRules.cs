using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Survival;
using Horyzont.Core.World;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Resolution of <see cref="SuitRefillIntent"/> into the compound <c>ItemDestroyOp(slot, type, 1, SuitRefill)</c> then <c>SuitRefillOp</c> (K6-34 D7); skeleton of step B1, the logic and the wiring in
/// <see cref="SurvivalRules"/> come in B4.
/// </summary>
internal static class SuitRules
{
    /// <summary>Resolves the intent; the checks of D7 in their order.</summary>
    /// <param name="state">The world.</param>
    /// <param name="content">The survival content.</param>
    /// <param name="rules">The hazard rules of the simulation.</param>
    /// <param name="player">The acting player.</param>
    /// <param name="intent">The intent.</param>
    /// <param name="operation">The operation when the method returns <see cref="RejectReason.None"/>.</param>
    /// <returns>The reason.</returns>
    internal static RejectReason Resolve(WorldState state, SurvivalContent content, HazardRules rules, PlayerId player, SuitRefillIntent intent, out Operation operation) =>
        throw new NotImplementedException("K6-34 B4");
}
