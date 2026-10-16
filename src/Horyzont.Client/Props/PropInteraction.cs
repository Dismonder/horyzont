using Horyzont.Client.Tools;
using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Props;
using Horyzont.Core.Simulation;

namespace Horyzont.Client.Props;

/// <summary>
/// Hitting props with the hand and the multitool (K6-31 D19): looks at the prop under the crosshair, keeps the prompt for the hint, makes the
/// <see cref="PropHitIntent"/> and counts the hits from the content (hit points and tiers; informational only, the truth is the harvest of
/// the server). The builder does not hit props. <see cref="ToolController"/> asks it before the terrain tool, paces the hits
/// (<see cref="PaceClass.Prop"/>) and sends them. Main thread only; steady state allocates nothing.
/// </summary>
/// <param name="picker">Finds the prop under the crosshair.</param>
/// <param name="states">The harvested props.</param>
/// <param name="content">The survival content (hit points and tiers of the props).</param>
/// <param name="held">The tool in the selected hotbar slot.</param>
/// <param name="rules">Game rules.</param>
public sealed class PropInteraction(PropPicker picker, IPropStateView states, SurvivalContent content, IHeldTool held, GameRules rules)
{
    private readonly PropPicker _picker = picker ?? throw new ArgumentNullException(nameof(picker));
    private readonly IPropStateView _states = states ?? throw new ArgumentNullException(nameof(states));
    private readonly SurvivalContent _content = content ?? throw new ArgumentNullException(nameof(content));
    private readonly IHeldTool _held = held ?? throw new ArgumentNullException(nameof(held));
    private readonly GameRules _rules = rules ?? throw new ArgumentNullException(nameof(rules));
    private PropPick _pick;
    private PropDef? _def;
    private PlanetId _progressPlanet;
    private long _lastHitTick;

    /// <summary>Gets the hint for the prop under the crosshair at the latest <see cref="Observe"/>, or null.</summary>
    public PropPrompt? Prompt { get; private set; }

    /// <summary>Gets the progress of the prop hit last, or null once it was harvested or the wear wore off.</summary>
    public PropProgress? Progress { get; private set; }

    /// <summary>Gets <see cref="Progress"/> when it belongs to the prop aimed at now (the HUD shows hits only for the prop under the crosshair), else null.</summary>
    public PropProgress? AimedProgress => Progress is { } progress && Prompt is not null && progress.Prop == _pick.Prop.Id ? progress : null;

    /// <summary>Gets a value indicating whether a prop is aimed at with a tool that hits props (a press belongs to the prop, not to the terrain).</summary>
    public bool HasTarget => Prompt is not null;

    /// <summary>Gets a value indicating whether the tool in hand is strong enough for the aimed prop.</summary>
    public bool CanHit => Prompt is { Kind: not PropPromptKind.ToolTierTooLow };

    /// <summary>Looks at the crosshair; call once per tick before the press is handled.</summary>
    /// <param name="aim">The aim.</param>
    /// <param name="world">World view.</param>
    /// <param name="tool">The tool selected.</param>
    /// <param name="clientTick">Client physics tick.</param>
    public void Observe(in AimRay aim, IToolWorldView world, ToolKind tool, long clientTick)
    {
        ArgumentNullException.ThrowIfNull(world);
        ExpireProgress(clientTick);
        Prompt = null;
        _def = null;
        if (tool == ToolKind.Builder || !_picker.TryPick(aim, world, _rules, out var pick) || !_content.TryGetProp(pick.Prop.Kind, out var def))
        {
            return;
        }

        _pick = pick;
        _def = def;
        var kind = _held.ToolTier < def.ToolTier ? PropPromptKind.ToolTierTooLow : KindOf(pick.Prop.Kind);
        Prompt = new PropPrompt(kind, pick.Prop.Kind, def.ToolTier);
    }

    /// <summary>Forgets what was aimed at (the aim is not usable this tick).</summary>
    public void Clear()
    {
        Prompt = null;
        _def = null;
    }

    /// <summary>Makes the intent for the aimed prop; call only when <see cref="CanHit"/>.</summary>
    /// <returns>The intent.</returns>
    /// <exception cref="InvalidOperationException">No prop is aimed at.</exception>
    public PropHitIntent CreateHit()
    {
        if (_def is null)
        {
            throw new InvalidOperationException("No prop is aimed at.");
        }

        return new PropHitIntent(_pick.Planet, _pick.Prop.Id, (ushort)Math.Clamp(_held.HotbarSlot, 0, byte.MaxValue));
    }

    /// <summary>Counts a hit that was sent: the damage is 1 + (tier of the tool - tier of the prop), the prop has its hit points.</summary>
    /// <param name="clientTick">Client physics tick.</param>
    public void NoteHit(long clientTick)
    {
        if (_def is not { } def)
        {
            return;
        }

        var damage = Math.Max(1, 1 + (_held.ToolTier - def.ToolTier));
        var total = (def.HitPoints + damage - 1) / damage;
        var hits = Progress is { } current && current.Prop == _pick.Prop.Id ? current.Hits + 1 : 1;
        Progress = new PropProgress(_pick.Prop.Id, Math.Min(hits, total), total);
        _progressPlanet = _pick.Planet;
        _lastHitTick = clientTick;
    }

    private static PropPromptKind KindOf(PropKindV1 kind) => kind switch
    {
        PropKindV1.Bush => PropPromptKind.CutBush,
        PropKindV1.Rock or PropKindV1.Boulder => PropPromptKind.BreakRock,
        PropKindV1.Crystals => PropPromptKind.MineCrystals,
        _ => PropPromptKind.CutTree,
    };

    private void ExpireProgress(long clientTick)
    {
        if (Progress is not { } progress)
        {
            return;
        }

        // Harvested (by us or by someone else), or left alone so long that the server forgot the wear (D10).
        if (_states.IsHarvested(_progressPlanet, progress.Prop) || clientTick - _lastHitTick > _rules.PropDamageResetTicks)
        {
            Progress = null;
        }
    }
}
