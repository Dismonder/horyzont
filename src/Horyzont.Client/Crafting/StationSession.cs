using Horyzont.Client.Blocks;
using Horyzont.Client.Inventory;
using Horyzont.Client.Session;
using Horyzont.Client.Tools;
using Horyzont.Core.BlockEntities;
using Horyzont.Core.Content;
using Horyzont.Core.Crafting;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Blocks;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Crafting;

/// <summary>
/// The station with time (furnace, refinery) that the local player has open (K6-32 D17, D23): subscribes to its input and output containers,
/// forgets them when the player closes the panel or the server ends the subscription, sends the choice of a recipe and take all, and computes the
/// progress bar from the start tick of the replicated state and the tick of the server.
/// </summary>
public sealed class StationSession : IClientSessionListener
{
    private readonly ClientInventory _inventory;
    private readonly IBlockEntityView _entities;
    private readonly SurvivalContent _content;
    private readonly IIntentSender _sender;
    private readonly Action<Message> _send;
    private readonly ServerTickReader _serverTick;
    private readonly GameRules _rules;
    private readonly ILogger<StationSession> _logger;
    private readonly HashSet<uint> _pending = [];
    private Phase _phase;
    private BlockRef _station;
    private ContainerId _input;
    private ContainerId _output;
    private bool _inputReady;
    private bool _outputReady;

    /// <summary>Initializes a new instance of the <see cref="StationSession"/> class.</summary>
    /// <param name="inventory">The inventory that stores the containers.</param>
    /// <param name="entities">The states of the functional blocks.</param>
    /// <param name="content">The game content with the recipes.</param>
    /// <param name="sender">Where intents go.</param>
    /// <param name="send">Sends a client message (subscribe, unsubscribe).</param>
    /// <param name="serverTick">The tick of the server.</param>
    /// <param name="rules">Game rules (tick rate).</param>
    /// <param name="logger">Logger.</param>
    public StationSession(
        ClientInventory inventory, IBlockEntityView entities, SurvivalContent content, IIntentSender sender, Action<Message> send,
        ServerTickReader serverTick, GameRules rules, ILogger<StationSession> logger)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _serverTick = serverTick ?? throw new ArgumentNullException(nameof(serverTick));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _inventory.StateApplied += OnState;
        _inventory.StateRefused += OnRefused;
    }

    private enum Phase : byte
    {
        Closed = 0,
        Opening = 1,
        Open = 2,
    }

    /// <summary>Raised when the states of both containers arrived and the panel may open.</summary>
    public event Action? Opened;

    /// <summary>Raised when the open station was closed by the player or the server (or the session ended).</summary>
    public event Action? Closed;

    /// <summary>Raised whenever something the panel shows changed.</summary>
    public event Action? Changed;

    /// <summary>
    /// Gets or sets a test for a station input that another session follows (the input of an oxygen generator is the container of the chest panel, K6-34); such a state is not dropped here.
    /// </summary>
    public Func<ContainerId, bool>? IsInputOwnedElsewhere { get; set; }

    /// <summary>Gets a value indicating whether both containers arrived and the panel is open.</summary>
    public bool IsOpen => _phase == Phase.Open;

    /// <summary>Gets a value indicating whether a station was requested and a container has not arrived.</summary>
    public bool IsOpening => _phase == Phase.Opening;

    /// <summary>Gets the kind of the requested or open station.</summary>
    public StationKind Kind { get; private set; }

    /// <summary>Gets the requested or open station block, null when closed.</summary>
    public BlockRef? Station => _phase == Phase.Closed ? null : _station;

    /// <summary>Gets the replicated input container of the open station, or null.</summary>
    public ClientContainer? Input => IsOpen && _inventory.TryGetContainer(_input, out var container) ? container : null;

    /// <summary>Gets the replicated output container of the open station, or null.</summary>
    public ClientContainer? Output => IsOpen && _inventory.TryGetContainer(_output, out var container) ? container : null;

    /// <summary>Gets the recipe the station works on, null when none is chosen.</summary>
    public RecipeDef? ActiveRecipe => State() is { Recipe.Length: > 0 } state && _content.TryGetRecipe(state.Recipe, out var recipe) ? recipe : null;

    /// <summary>Gets the part of the current piece done, 0 to 1, or null when the station is idle or the tick of the server is unknown.</summary>
    public double? Progress
    {
        get
        {
            if (State() is not { Started: { } started } || ActiveRecipe is not { TimeSeconds: > 0 } recipe || !_serverTick(out var tick))
            {
                return null;
            }

            return StationProcess.Progress(started, new Tick(tick), (ulong)recipe.TimeSeconds * (ulong)_rules.TickRate);
        }
    }

    /// <summary>Gets the reason of the latest refusal, <see cref="RejectReason.None"/> after a success.</summary>
    public RejectReason LastRefusal { get; private set; }

    /// <summary>Asks for the containers of a station; closes the station that is open.</summary>
    /// <param name="kind">Station kind.</param>
    /// <param name="station">The station block.</param>
    /// <returns>False when the state of the block is not replicated yet (nothing was sent).</returns>
    public bool Open(StationKind kind, BlockRef station)
    {
        if (!_entities.TryGet(station.Construct, station.Cell, out var found) || found is not StationBlockState state)
        {
            return false;
        }

        if (_phase != Phase.Closed)
        {
            if (_station == station)
            {
                return true;
            }

            Close();
        }

        Kind = kind;
        _station = station;
        _input = state.Input;
        _output = state.Output;
        _inputReady = false;
        _outputReady = false;
        _phase = Phase.Opening;
        LastRefusal = RejectReason.None;
        _send(new ContainerSubscribe(_input));
        _send(new ContainerSubscribe(_output));
        _logger.StationRequested(station.Construct.Value, _input.Value, _output.Value);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Closes the station and tells the server.</summary>
    public void Close()
    {
        if (_phase == Phase.Closed)
        {
            return;
        }

        var wasOpen = _phase == Phase.Open;
        _send(new ContainerUnsubscribe(_input));
        _send(new ContainerUnsubscribe(_output));
        _logger.StationClosed(_station.Construct.Value);
        End(RejectReason.None);
        if (wasOpen)
        {
            Closed?.Invoke();
        }

        Changed?.Invoke();
    }

    /// <summary>Chooses the recipe of the station.</summary>
    /// <param name="recipe">A recipe of the station kind.</param>
    /// <returns>True when an intent was sent.</returns>
    public bool SelectRecipe(RecipeDef recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (!IsOpen || recipe.Station != Kind || ActiveRecipe?.Key == recipe.Key)
        {
            return false;
        }

        var intent = new CraftIntent(recipe.Key, 1, _station);
        return Submit(intent);
    }

    /// <summary>Takes everything from the output container.</summary>
    /// <returns>True when an intent was sent.</returns>
    public bool TakeAll() => IsOpen && Submit(new TakeAllIntent(_output));

    /// <inheritdoc />
    public void OnIntentReply(IntentReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (!_pending.Remove(reply.ClientSeq))
        {
            return;
        }

        LastRefusal = reply.Reason;
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code)
    {
        var wasOpen = _phase == Phase.Open;
        End(RejectReason.None);
        if (wasOpen)
        {
            Closed?.Invoke();
        }

        Changed?.Invoke();
    }

    private StationBlockState? State() =>
        _phase != Phase.Closed && _entities.TryGet(_station.Construct, _station.Cell, out var state) ? state as StationBlockState : null;

    private bool Submit(Intent intent)
    {
        if (!_sender.CanSend || _pending.Count >= CraftingSession.MaxPending)
        {
            return false;
        }

        _pending.Add(_sender.Submit(intent));
        Changed?.Invoke();
        return true;
    }

    private void End(RejectReason refusal)
    {
        _phase = Phase.Closed;
        _inputReady = false;
        _outputReady = false;
        _pending.Clear();
        _inventory.Forget(_input);
        _inventory.Forget(_output);
        LastRefusal = refusal;
    }

    private void OnState(ClientContainer container)
    {
        if (container.Kind is not (ContainerKind.StationInput or ContainerKind.StationOutput))
        {
            return;
        }

        var ours = container.Id == (container.Kind == ContainerKind.StationInput ? _input : _output);
        if (_phase == Phase.Closed || !ours)
        {
            // A state that arrives after the panel was closed (or for another station): not followed any more, unless another session follows it.
            if (container.Kind != ContainerKind.StationInput || IsInputOwnedElsewhere?.Invoke(container.Id) != true)
            {
                _inventory.Forget(container.Id);
            }

            return;
        }

        if (container.Kind == ContainerKind.StationInput)
        {
            _inputReady = true;
        }
        else
        {
            _outputReady = true;
        }

        var opened = false;
        if (_phase == Phase.Opening && _inputReady && _outputReady)
        {
            _phase = Phase.Open;
            LastRefusal = RejectReason.None;
            opened = true;
            _logger.StationOpened(_station.Construct.Value);
        }

        if (opened)
        {
            Opened?.Invoke();
        }

        Changed?.Invoke();
    }

    private void OnRefused(ContainerId container, RejectReason reason)
    {
        if (_phase == Phase.Closed || (container != _input && container != _output))
        {
            return;
        }

        var wasOpen = _phase == Phase.Open;
        _logger.StationRefused(container.Value, reason);

        // The server ended one subscription; the other one is ended on its side too, and a repeated unsubscribe changes nothing.
        _send(new ContainerUnsubscribe(container == _input ? _output : _input));
        End(reason);
        if (wasOpen)
        {
            Closed?.Invoke();
        }

        Changed?.Invoke();
    }
}
