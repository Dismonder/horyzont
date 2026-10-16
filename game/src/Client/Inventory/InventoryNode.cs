using Godot;

using Horyzont.Client.Blocks;
using Horyzont.Client.Crafting;
using Horyzont.Client.Inventory;
using Horyzont.Client.Session;
using Horyzont.Client.Tools;
using Horyzont.Client.UI;
using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Net.Session;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Client.Inventory;

/// <summary>
/// The inventory of the local player, a child of <c>/Root/Client</c> of every client with a character (K6-29W, D7): the replicated
/// containers, the hotbar selection, the mouse logic, the toasts and, when the game renders, the hotbar, the inventory screen and the
/// toast column. It holds client state only; every move is an intent that the server answers (decision D15).
/// </summary>
public sealed partial class InventoryNode : Node, IClientSessionListener
{
    private readonly Dictionary<ushort, string> _names = [];
    private readonly SwitchableBlockEntityView _stationView = new();
    private ServerTickReader _serverTick = static (out ulong tick) =>
    {
        tick = 0;
        return false;
    };
    private ClientRuntime? _client;
    private SurvivalContent? _content;
    private ILoggerFactory? _loggers;
    private bool _render;
    private Action? _recaptureMouse;
    private uint _version;

    /// <summary>Raised on the main thread when the inventory screen opens or closes.</summary>
    public event Action? OpenChanged;

    /// <summary>Gets or sets where the keys and the wheel come from; the reader child by default, a script in tests.</summary>
    public IInventoryInputSource InputSource { get; set; } = new NoInventoryInput();

    /// <summary>
    /// Gets or sets the gate of the other modal screens (the menu and the map): while it blocks input the inventory does not open, and
    /// closing it does not take the mouse back from them. Null for none.
    /// </summary>
    public IGameplayInputGate? ModalGate { get; set; }

    /// <summary>Gets the containers of the player.</summary>
    public ClientInventory Inventory { get; private set; } = null!;

    /// <summary>Gets the hotbar selection.</summary>
    public HotbarSelection Selection { get; } = new();

    /// <summary>Gets the mouse logic of the inventory screen.</summary>
    public InventoryInteraction Interaction { get; private set; } = null!;

    /// <summary>Gets the chest that is open on the screen (K6-30): E on a chest opens the screen with its panel.</summary>
    public ChestSession Chests { get; private set; } = null!;

    /// <summary>Gets the discoveries and the progress keys the server told (K6-32); the replication feeds it.</summary>
    public ClientProgress Progress { get; private set; } = null!;

    /// <summary>Gets the recipes the player knows.</summary>
    public RecipeList Recipes { get; private set; } = null!;

    /// <summary>Gets the crafting of the screen: the hands, or a workbench-like station that E opened (K6-32).</summary>
    public CraftingSession Crafting { get; private set; } = null!;

    /// <summary>Gets the furnace or refinery that is open on the screen (K6-32): E on one opens the screen with its panel.</summary>
    public StationSession Stations { get; private set; } = null!;

    /// <summary>Gets the toasts.</summary>
    public PickupToastFeed Feed { get; } = new();

    /// <summary>Gets the visuals of the items.</summary>
    public ItemVisuals Visuals { get; private set; } = null!;

    /// <summary>Gets the survival content the node was given (item definitions, yields, props); null before <c>Initialize</c>.</summary>
    public SurvivalContent? Content => _content;

    /// <summary>Gets the tool that each item selects.</summary>
    public ItemToolMap ToolMap { get; private set; } = ItemToolMap.Default;

    /// <summary>Gets a value indicating whether the inventory screen is open: movement, camera and tools read no input then.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Gets a counter that grows whenever something the UI shows changed (containers, selection, hand, open state).</summary>
    public uint UiVersion => _version;

    /// <summary>Gets the time source of the toasts, milliseconds; tests replace it.</summary>
    public Func<long> Clock { get; set; } = static () => (long)Time.GetTicksMsec();

    /// <summary>Gives the node the client runtime of the game; call before the node enters the tree.</summary>
    /// <param name="client">The client runtime.</param>
    /// <param name="content">The survival content (item definitions and yields).</param>
    /// <param name="visuals">Item visuals.</param>
    /// <param name="loggers">Loggers.</param>
    /// <param name="render">Whether the UI is built (false for headless runs).</param>
    /// <param name="recaptureMouse">Takes the mouse back for the game when the screen closes.</param>
    public void Initialize(
        ClientRuntime client, SurvivalContent content, ItemVisuals visuals, ILoggerFactory loggers, bool render, Action? recaptureMouse)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        Initialize(
            content, visuals, new SessionIntentSender(client.Session),
            id =>
            {
                if (client.Session.State == ClientState.InGame)
                {
                    client.Session.Send(new ContainerSubscribe(id));
                }
            },
            loggers, render, recaptureMouse,
            message =>
            {
                if (client.Session.State == ClientState.InGame)
                {
                    client.Session.Send(message);
                }
            });
    }

    /// <summary>Gives the node its parts without a session; tests send intents to a fake.</summary>
    /// <param name="content">The survival content.</param>
    /// <param name="visuals">Item visuals.</param>
    /// <param name="sender">Where the intents of the screen go.</param>
    /// <param name="requestState">Asks the server for a whole container.</param>
    /// <param name="loggers">Loggers.</param>
    /// <param name="render">Whether the UI is built.</param>
    /// <param name="recaptureMouse">Takes the mouse back for the game when the screen closes.</param>
    /// <param name="send">Sends a client message (the subscription to a chest); messages are dropped when null.</param>
    public void Initialize(
        SurvivalContent content, ItemVisuals visuals, IIntentSender sender, Action<ContainerId> requestState, ILoggerFactory loggers, bool render,
        Action? recaptureMouse, Action<Message>? send = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(visuals);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(requestState);
        ArgumentNullException.ThrowIfNull(loggers);
        _content = content;
        _loggers = loggers;
        _render = render;
        _recaptureMouse = recaptureMouse;
        Visuals = visuals;
        ToolMap = visuals.ToolMap();
        Inventory = new ClientInventory(content.Items, requestState, loggers.CreateLogger<ClientInventory>());
        Chests = new ChestSession(Inventory, sender, send ?? (static _ => { }), loggers.CreateLogger<ChestSession>());
        var rules = new GameRules();
        Progress = new ClientProgress();
        Recipes = new RecipeList(content, Inventory, Progress);
        Crafting = new CraftingSession(sender, rules, loggers.CreateLogger<CraftingSession>());
        Stations = new StationSession(
            Inventory, _stationView, content, sender, send ?? (static _ => { }), (out ulong tick) => _serverTick(out tick), rules,
            loggers.CreateLogger<StationSession>());
        Interaction = new InventoryInteraction(Inventory, sender, loggers.CreateLogger<InventoryInteraction>(), Chests, Stations);
        Inventory.Changed += Refresh;
        Chests.Changed += Refresh;
        Chests.Opened += OnChestOpened;
        Chests.Closed += OnChestClosed;
        Progress.Changed += Refresh;
        Crafting.Changed += Refresh;
        Crafting.Opened += OnStationOpened;
        Crafting.Closed += OnChestClosed;
        Stations.Changed += Refresh;
        Stations.Opened += OnStationOpened;
        Stations.Closed += OnChestClosed;
        Inventory.OwnedCountChanged += (type, delta) => Feed.OnOwnedCountChanged(type, delta, Clock());
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        if (_content is null)
        {
            throw new InvalidOperationException("InventoryNode.Initialize was not called before the node entered the tree.");
        }

        if (InputSource is NoInventoryInput && GetNodeOrNull<InventoryInputReader>("Input") is { } reader)
        {
            InputSource = reader;
        }

        if (_render)
        {
            AddChild(new HotbarHud(this));
            AddChild(new InventoryScreen(this));
            AddChild(new PickupToasts(this));
        }

        if (_client is not null)
        {
            _client.AddListener(Inventory);
            _client.AddListener(Interaction);
            _client.AddListener(Chests);
            _client.AddListener(Crafting);
            _client.AddListener(Stations);
            _client.AddListener(this);
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_client is not null)
        {
            _client.RemoveListener(Inventory);
            _client.RemoveListener(Interaction);
            _client.RemoveListener(Chests);
            _client.RemoveListener(Crafting);
            _client.RemoveListener(Stations);
            _client.RemoveListener(this);
        }
    }

    /// <inheritdoc />
    public override void _Notification(int what)
    {
        if (what == NotificationTranslationChanged)
        {
            _names.Clear();
            Refresh();
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        var input = InputSource.Take();
        if (input.HotbarKey >= 0)
        {
            Selection.SelectKey(input.HotbarKey);
            Refresh();
        }

        if (input.WheelSteps != 0 && !IsOpen)
        {
            Selection.Step(input.WheelSteps);
            Refresh();
        }

        if (input.Toggle)
        {
            Toggle();
        }
        else if (input.Escape && IsOpen)
        {
            EscapePressed();
        }

        Feed.Update(Clock());
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code)
    {
        Close();
        Refresh();
    }

    /// <summary>Opens the screen when the containers are known, closes it otherwise.</summary>
    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    /// <summary>Opens the inventory screen and releases the mouse.</summary>
    public void Open()
    {
        if (IsOpen || Inventory.Bag is null || ModalGate?.IsBlocked == true)
        {
            return;
        }

        IsOpen = true;
        if (DisplayServer.GetName() != "headless")
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }

        Refresh();
        OpenChanged?.Invoke();
    }

    /// <summary>Closes the inventory screen, puts the items in hand back and takes the mouse back for the game.</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        Interaction.Cancel();
        IsOpen = false;
        Chests.Close();
        Crafting.Close();
        Stations.Close();
        if (ModalGate?.IsBlocked != true)
        {
            _recaptureMouse?.Invoke();
        }

        Refresh();
        OpenChanged?.Invoke();
    }

    private void OnChestOpened()
    {
        Open();
        if (!IsOpen)
        {
            // The screen cannot open now (a modal screen has focus, the bag is unknown): do not keep following the chest.
            Chests.Close();
        }
    }

    private void OnStationOpened()
    {
        Open();
        if (!IsOpen)
        {
            // The screen cannot open now (a modal screen has focus, the bag is unknown): do not keep following the station.
            Crafting.Close();
            Stations.Close();
        }
    }

    /// <summary>
    /// Tells the screen where the states of the station blocks and the tick of the server come from (K6-32). The composition calls it
    /// once it knows them: a host reads them from the world it shares with the server, a remote client from the replication.
    /// </summary>
    /// <param name="entities">The states of the functional blocks.</param>
    /// <param name="serverTick">The tick of the server, from the same source as the HUD.</param>
    public void ConfigureStations(IBlockEntityView entities, ServerTickReader serverTick)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(serverTick);
        _stationView.Inner = entities;
        _serverTick = serverTick;
    }

    private void OnChestClosed()
    {
        // The server ended the subscription (the player walked away, the chest became private or was removed): the screen goes with it.
        Close();
    }

    /// <summary>Escape on the open screen: withdraws the discard question, then the hand, then closes.</summary>
    public void EscapePressed()
    {
        if (Interaction.AwaitingDiscardConfirmation)
        {
            Interaction.CancelDiscard();
        }
        else if (Interaction.Held is not null)
        {
            Interaction.Cancel();
        }
        else
        {
            Close();
            return;
        }

        Refresh();
    }

    /// <summary>A click on a slot of the screen.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="button">The mouse button.</param>
    /// <param name="shift">Whether Shift was held.</param>
    public void OnSlotClicked(SlotRef slot, MouseButton button, bool shift)
    {
        if (button == MouseButton.Left)
        {
            _ = shift ? Interaction.ShiftClick(slot) : Interaction.LeftClick(slot);
        }
        else if (button == MouseButton.Right)
        {
            _ = Interaction.RightClick(slot);
        }

        Refresh();
    }

    /// <summary>Tells the UI that something it shows changed.</summary>
    public void Refresh() => _version++;

    /// <summary>Reports that the server accepted a dig of the terrain tool (for the "Inventory full" toast).</summary>
    public void OnDigAccepted() => Feed.OnDigAccepted(Clock(), !Inventory.HasRoomForResources());

    /// <inheritdoc />
    public void OnIntentReply(IntentReply reply) => Refresh();

    /// <summary>The tool the selected hotbar slot selects.</summary>
    /// <param name="kind">The tool, <see cref="ToolKind.None"/> for a slot without one.</param>
    /// <returns>False while the hotbar has not arrived (the tool then stays as it is).</returns>
    public bool TryGetSelectedTool(out ToolKind kind)
    {
        kind = ToolKind.None;
        if (Inventory.Hotbar is null)
        {
            return false;
        }

        kind = ToolMap.Resolve(Inventory, Selection);
        return true;
    }

    /// <summary>How many items of the resource that fills a terrain material the player carries.</summary>
    /// <param name="materialKey">Terrain material key (for example <c>core:dirt</c>).</param>
    /// <param name="itemName">Translated name of the resource.</param>
    /// <param name="count">Items in the bag and the hotbar.</param>
    /// <returns>False when the material yields no item.</returns>
    public bool TryGetFillStock(string materialKey, out string itemName, out int count)
    {
        itemName = string.Empty;
        count = 0;
        if (_content is null || !_content.TryGetYield(materialKey, out var yield) || !Inventory.TryGetTypeId(yield.ItemKey, out var type))
        {
            return false;
        }

        itemName = ItemName(type);
        count = Inventory.CountOf(type);
        return true;
    }

    /// <summary>The translated name of an item type.</summary>
    /// <param name="type">Item type.</param>
    /// <returns>The name, or the key when the locale has no translation.</returns>
    public string ItemName(ItemTypeId type)
    {
        if (_names.TryGetValue(type.Value, out var cached))
        {
            return cached;
        }

        var key = Inventory.KeyOf(type);
        string name;
        if (key is null)
        {
            name = Tr("ITEM_UNKNOWN");
        }
        else
        {
            var translationKey = "ITEM_" + key.ToUpperInvariant().Replace(':', '_');
            var translated = Tr(translationKey);
            name = string.Equals(translated, translationKey, StringComparison.Ordinal) ? key : translated;
        }

        _names[type.Value] = name;
        return name;
    }

    /// <summary>The visual of an item type.</summary>
    /// <param name="type">Item type.</param>
    /// <returns>The visual.</returns>
    public ItemVisual VisualOf(ItemTypeId type) => Visuals.Get(Inventory.KeyOf(type));

    /// <summary>The translated category of an item.</summary>
    /// <param name="category">The category.</param>
    /// <returns>The text.</returns>
    public string CategoryName(ItemCategory category) => Tr("INV_CATEGORY_" + category.ToString().ToUpperInvariant());

    private sealed class NoInventoryInput : IInventoryInputSource
    {
        public InventoryInputFrame Take() => InventoryInputFrame.None;
    }
}
