using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Ships;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Ships;

/// <summary>What <see cref="ShipModeSwitch.Toggle"/> did.</summary>
public enum ShipModeToggleResult : byte
{
    /// <summary>A <see cref="ShipModeIntent"/> went to the server.</summary>
    Sent = 0,

    /// <summary>No usable aim this tick.</summary>
    NoAim = 1,

    /// <summary>The crosshair is not on a construct within reach.</summary>
    NoConstruct = 2,

    /// <summary>The aimed block is not a cockpit.</summary>
    NotACockpit = 3,

    /// <summary>The session cannot send now.</summary>
    CannotSend = 4,

    /// <summary>Too many intents wait for the server.</summary>
    Waiting = 5,
}

/// <summary>A construct with a cockpit under the crosshair and what the client computes for it (a preview: the server decides).</summary>
/// <param name="Construct">Construct id.</param>
/// <param name="Mode">Its mode as the client sees it.</param>
/// <param name="Stats">Mass, thrust and seats from the client's own copy of the blocks and the content.</param>
public sealed record AimedShip(EntityId Construct, ConstructMode Mode, ShipStats Stats);

/// <summary>
/// The ship mode key of the local player (K5-23): when the crosshair is on a cockpit of a construct within reach, sends a
/// <see cref="ShipModeIntent"/> for the opposite mode. The ray starts at the eye (<see cref="AimRay.Eye"/>, the server formula).
/// The answer comes back as an intent reply that the tool tracker settles; its refusal shows in the HUD. Also keeps
/// <see cref="Aimed"/> for the developer HUD, recomputing the stats only when the construct changes.
/// </summary>
public sealed class ShipModeSwitch
{
    private readonly IToolWorldView _world;
    private readonly IIntentSender _sender;
    private readonly PendingIntentTracker _pending;
    private readonly BlockContentTable _content;
    private readonly GameRules _rules;
    private EntityId _statsConstruct;
    private uint _statsVersion;
    private ShipStats? _stats;
    private AimedShip? _aimed;

    /// <summary>Initializes a new instance of the <see cref="ShipModeSwitch"/> class.</summary>
    /// <param name="world">World view of the tools.</param>
    /// <param name="sender">Where intents go.</param>
    /// <param name="pending">Tracker that settles the reply.</param>
    /// <param name="content">Block definitions of the client content, indexed by the ids of the server registry.</param>
    /// <param name="rules">Game rules (reach).</param>
    public ShipModeSwitch(IToolWorldView world, IIntentSender sender, PendingIntentTracker pending, BlockContentTable content, GameRules rules)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
    }

    /// <summary>Gets the construct with a cockpit under the crosshair at the latest <see cref="Observe"/>, or null.</summary>
    public AimedShip? Aimed { get; private set; }

    /// <summary>The aim is not usable this tick: nothing is aimed at.</summary>
    public void ClearAim() => Aimed = null;

    /// <summary>Looks at the crosshair; call at a low rate (the stats are recomputed only for a new construct or version).</summary>
    /// <param name="aim">Aim of the local player.</param>
    public void Observe(in AimRay aim)
    {
        Aimed = null;
        if (!TryFind(aim, out var construct, out _))
        {
            return;
        }

        if (_stats is null || _statsConstruct != construct.Id || _statsVersion != construct.Version)
        {
            _stats = ShipStatsCalculator.Compute(construct, _content);
            _statsConstruct = construct.Id;
            _statsVersion = construct.Version;
        }

        if (_stats.Cockpits > 0)
        {
            _aimed = _aimed is not null && _aimed.Construct == construct.Id && _aimed.Mode == construct.Mode && ReferenceEquals(_aimed.Stats, _stats)
                ? _aimed
                : new AimedShip(construct.Id, construct.Mode, _stats);
            Aimed = _aimed;
        }
    }

    /// <summary>Sends the intent for the opposite mode when the crosshair is on a cockpit.</summary>
    /// <param name="aim">Aim of the local player.</param>
    /// <param name="clientTick">Client physics tick.</param>
    /// <returns>What happened.</returns>
    public ShipModeToggleResult Toggle(in AimRay aim, long clientTick)
    {
        if (!_sender.CanSend)
        {
            return ShipModeToggleResult.CannotSend;
        }

        if (!TryFind(aim, out var construct, out var cell))
        {
            return ShipModeToggleResult.NoConstruct;
        }

        if (_content.Get(construct.GetBlock(cell).TypeId).Function.Kind != BlockFunctionKind.Cockpit)
        {
            return ShipModeToggleResult.NotACockpit;
        }

        if (_pending.IsFull)
        {
            return ShipModeToggleResult.Waiting;
        }

        var target = construct.Mode == ConstructMode.Ship ? ConstructMode.Static : ConstructMode.Ship;
        var intent = new ShipModeIntent(construct.Id, cell, target, aim.Eye, aim.Direction);
        var seq = _sender.Submit(intent);
        _pending.Add(seq, intent, default, clientTick);
        return ShipModeToggleResult.Sent;
    }

    private bool TryFind(in AimRay aim, out Construct construct, out CellPos cell)
    {
        construct = null!;
        cell = default;
        if (!_world.RaycastConstructs(aim.Planet, aim.Eye, aim.Direction, _rules.ReachMeters, out var hit)
            || !_world.TryGetConstruct(hit.Construct, out var found))
        {
            return false;
        }

        construct = found;
        cell = hit.Cell;
        return true;
    }
}
