using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.Survival;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Survival;

/// <summary>
/// Health, stamina and rest of the players online (K6-33 D13, D15, D16, D18): every tick, in the phase <see cref="ServerTickPhase.Compose"/> (after the bodies moved),
/// it reads the motion of each body, the food and the pending costs and damage, advances <see cref="VitalsStep"/> and stores the result with
/// <see cref="ServerSimulation.UpdatePlayerVitals"/> (outside the journal and the hash). Every <see cref="VitalsRules.VitalsSendEveryTicks"/> ticks each ready player
/// gets message 44 about itself. Registered only for a world with survival content. The numbers come from <see cref="ServerSimulation.VitalsRules"/>, the same
/// instance the resolver of eating uses. With the hazard content (K6-34) it also steps the suit of each player (<see cref="SuitStep"/>, the surroundings from a <see cref="HazardTracker"/>), adds the damage at zero
/// to the same tick and sends the suit tail of the message. Main thread only; the tick path allocates nothing except the message.
/// </summary>
public sealed partial class VitalsService : IServerTickParticipant, IServerPlayerObserver
{
    private readonly Func<ServerSimulation?> _simulation;
    private readonly IVitalsMotionSource _motion;
    private readonly Action<PlayerId, Message> _send;
    private readonly Func<bool> _isReadOnly;
    private readonly ILogger _logger;
    private readonly Dictionary<PlayerId, PlayerData> _data = [];
    private ServerSimulation? _tableFor;
    private FoodTable? _table;
    private SuitModuleTable? _modules;
    private HazardTracker? _hazards;

    /// <summary>Initializes a new instance of the <see cref="VitalsService"/> class.</summary>
    /// <param name="simulation">The simulation of the running world (null before the world has started).</param>
    /// <param name="motion">Where the motion of the bodies is read from.</param>
    /// <param name="send">Sends a message to one player.</param>
    /// <param name="isReadOnly">True when the server must not change the world (safe mode, shutdown).</param>
    /// <param name="logger">Logger, null for none.</param>
    public VitalsService(
        Func<ServerSimulation?> simulation, IVitalsMotionSource motion, Action<PlayerId, Message> send, Func<bool> isReadOnly, ILogger<VitalsService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(isReadOnly);
        _simulation = simulation;
        _motion = motion;
        _send = send;
        _isReadOnly = isReadOnly;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public ServerTickPhase Phase => ServerTickPhase.Compose;

    /// <summary>Gets the number of messages 44 sent.</summary>
    public int MessagesSent { get; private set; }

    /// <summary>Creates the service for a runtime that has not started yet, as a tick participant and a player observer; register it after the inventory service and the progress replication.</summary>
    /// <param name="runtime">The runtime.</param>
    /// <param name="motion">Where the motion of the bodies is read from.</param>
    /// <param name="loggers">Loggers, null for none.</param>
    /// <returns>The service.</returns>
    public static VitalsService Register(ServerRuntime runtime, IVitalsMotionSource motion, ILoggerFactory? loggers = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var service = new VitalsService(
            () => runtime.RuntimeState is ServerRuntimeState.Running or ServerRuntimeState.SafeMode ? runtime.Simulation : null,
            motion,
            (player, message) => runtime.Session.Send(player, message),
            () => runtime.RuntimeState != ServerRuntimeState.Running,
            loggers?.CreateLogger<VitalsService>());
        runtime.AddTickParticipant(service);
        runtime.AddPlayerObserver(service);
        return service;
    }

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Step();
    }

    /// <summary>Hurts a player; the damage is taken at the next tick (K6-33 D13). A damage that is not a positive finite number is ignored.</summary>
    /// <param name="player">The player.</param>
    /// <param name="amount">Health points.</param>
    /// <param name="kind">What hurt the player (for later packages that react to it).</param>
    public void ApplyDamage(PlayerId player, float amount, DamageKind kind)
    {
        if (float.IsFinite(amount) && amount > 0f && IsOnline(player))
        {
            Data(player).PendingDamage += amount;
        }
    }

    /// <summary>Spends stamina at once when the player has enough; otherwise changes nothing. Spending down to 0 does not set the exhaustion at once: <c>VitalsStep</c> sets it in the next tick, so <see cref="IsExhausted"/> still answers false for one tick.</summary>
    /// <param name="player">The player.</param>
    /// <param name="amount">Stamina points.</param>
    /// <returns>Whether the stamina was spent.</returns>
    public bool TrySpendStamina(PlayerId player, float amount)
    {
        if (!float.IsFinite(amount) || amount < 0f || _simulation() is not { } sim || !sim.State.Players.TryGet(player, out var state) || state.Vitals is not { } vitals || vitals.Stamina < amount)
        {
            return false;
        }

        if (!sim.UpdatePlayerVitals(player, vitals with { Stamina = vitals.Stamina - amount }))
        {
            return false;
        }

        Data(player).LastStaminaCost = sim.State.CurrentTick;
        return true;
    }

    /// <summary>Whether the player is exhausted (the bodies ignore sprint and jump, D20).</summary>
    /// <param name="player">The player.</param>
    /// <returns>True when exhausted.</returns>
    public bool IsExhausted(PlayerId player) =>
        _simulation() is { } sim && sim.State.Players.TryGet(player, out var state) && state.Vitals is { Exhausted: true };

    /// <inheritdoc />
    public void OnPlayerReady(PlayerId player) => Data(player).Ready = true;

    /// <inheritdoc />
    public void OnPlayerLeaving(PlayerId player, DisconnectCode reason)
    {
        _data.Remove(player);
        _hazards?.Forget(player);
    }

    /// <inheritdoc />
    public void OnIntentAccepted(PlayerId player, Intent intent)
    {
        if (_simulation() is not { Survival: not null } sim || !sim.State.Players.TryGet(player, out var state) || !state.IsOnline)
        {
            return;
        }

        switch (intent)
        {
            case TerrainToolIntent or PropHitIntent:
                Data(player).PendingCost += sim.VitalsRules.ToolUseStamina;
                break;
            case BlockInteractIntent { Action: BlockInteractAction.Sleep } sleep when RestTracker.TryBeginLying(sim, state, sleep, Data(player).Rest):
                _logger.LyingStarted(player.Value, sleep.Construct.Value);
                break;
        }
    }
}
