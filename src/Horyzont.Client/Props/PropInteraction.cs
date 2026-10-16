#pragma warning disable CA1822 // Skeleton of step D1 (K6-31); removed with the implementation of D2.
using Horyzont.Client.Tools;
using Horyzont.Core.Content;
using Horyzont.Core.Intents;
using Horyzont.Core.Simulation;

namespace Horyzont.Client.Props;

/// <summary>
/// Hitting props with the hand and the multitool (K6-31 D19): what is aimed at, the prompt, the intent and the local progress. Skeleton of
/// step D1: the behaviour comes with D2.
/// </summary>
/// <param name="picker">Finds the prop under the crosshair.</param>
/// <param name="states">The harvested props.</param>
/// <param name="content">The survival content (hit points and tiers of the props).</param>
/// <param name="held">The tool in the selected hotbar slot.</param>
/// <param name="rules">Game rules.</param>
public sealed class PropInteraction(PropPicker picker, IPropStateView states, SurvivalContent content, IHeldTool held, GameRules rules)
{
    /// <summary>Gets the hint for the prop under the crosshair at the latest <see cref="Observe"/>, or null.</summary>
    public PropPrompt? Prompt => null;

    /// <summary>Gets the progress of the prop hit last, or null.</summary>
    public PropProgress? Progress => null;

    /// <summary>Gets a value indicating whether a prop is aimed at with a tool that hits props (a press belongs to the prop, not to the terrain).</summary>
    public bool HasTarget => false;

    /// <summary>Gets a value indicating whether the tool in hand is strong enough for the aimed prop.</summary>
    public bool CanHit => false;

    /// <summary>Looks at the crosshair; call once per tick before the press is handled.</summary>
    /// <param name="aim">The aim.</param>
    /// <param name="world">World view.</param>
    /// <param name="tool">The tool selected.</param>
    /// <param name="clientTick">Client physics tick.</param>
    public void Observe(in AimRay aim, IToolWorldView world, ToolKind tool, long clientTick)
    {
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(world);
    }

    /// <summary>Forgets what was aimed at (the aim is not usable this tick).</summary>
    public void Clear()
    {
    }

    /// <summary>Makes the intent for the aimed prop; call only when <see cref="CanHit"/>.</summary>
    /// <returns>The intent.</returns>
    public PropHitIntent CreateHit() => throw new NotImplementedException("K6-31 D2");

    /// <summary>Counts a hit that was sent.</summary>
    /// <param name="clientTick">Client physics tick.</param>
    public void NoteHit(long clientTick)
    {
    }
}
