using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Resolution of crafting and the stations with time (K6-32 D12 to D14, D17, D18): <see cref="CraftIntent"/>, putting up and taking down a
/// station with time, the recipe choice. Skeleton: the body arrives with step B4. Called by <see cref="ServerSimulation"/> before the functional
/// block rules; the order of the checks is in docs/plans/K6-32-rzemioslo-i-stacje.md, "Walidacja i rozwiązanie intencji".
/// </summary>
internal sealed class CraftingRules(WorldState state, GameRules rules, BlockContentTable content, SurvivalContent? survival)
{
    /// <summary>Resolves the intents this class owns.</summary>
    /// <param name="player">Acting player.</param>
    /// <param name="intent">The validated intent.</param>
    /// <param name="facts">Facts of the validator.</param>
    /// <param name="operation">The operation when <paramref name="reason"/> is <see cref="RejectReason.None"/>.</param>
    /// <param name="reason">The refusal, or none.</param>
    /// <returns>False when the intent is not a crafting intent (the caller resolves it otherwise).</returns>
    internal bool TryResolve(PlayerId player, Intent intent, IntentFacts facts, out Operation operation, out RejectReason reason) =>
        throw new NotImplementedException("K6-32 B4");

    /// <summary>The world the rules read.</summary>
    internal WorldState State => state;

    /// <summary>Game rules.</summary>
    internal GameRules Rules => rules;

    /// <summary>Block content.</summary>
    internal BlockContentTable Content => content;

    /// <summary>Survival content.</summary>
    internal SurvivalContent? Survival => survival;
}
