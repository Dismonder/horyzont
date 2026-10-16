using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;

namespace Horyzont.Core.Simulation;

/// <summary>Resolution of <see cref="EatIntent"/> into the compound of eating (K6-33 D10, D11). The logic arrives with step B4.</summary>
internal static class FoodRules
{
    internal static RejectReason Resolve(PlayerId player, EatIntent intent, out Operation operation) =>
        throw new NotImplementedException("K6-33 B4");
}
