using Horyzont.Client.Tools;
using Horyzont.Core.Simulation;

namespace Horyzont.Client.Props;

/// <summary>Finds the prop under the crosshair (K6-31 D19). Skeleton of step D1: the behaviour comes with D2.</summary>
/// <param name="source">Where the props around the eye come from.</param>
/// <param name="states">The harvested props.</param>
public sealed class PropPicker(IPropSource source, IPropStateView states)
{
    /// <summary>Finds the nearest prop whose body the ray enters before the terrain, within the reach of the client.</summary>
    /// <param name="aim">The aim.</param>
    /// <param name="world">World view (terrain).</param>
    /// <param name="rules">Game rules (reach).</param>
    /// <param name="pick">The prop.</param>
    /// <returns>True when there is one.</returns>
    public bool TryPick(in AimRay aim, IToolWorldView world, GameRules rules, out PropPick pick)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rules);
        pick = default;
        return false;
    }
}
