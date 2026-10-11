using System.Globalization;

using Godot;

using Horyzont.Client.UI;

namespace Horyzont.Game.UI;

/// <summary>The list of saved worlds with the creation of a new one; in host mode also the lobby options.</summary>
public sealed partial class WorldListPanel : Control
{
    private readonly List<Guid> _ids = [];
    private bool _busy;

    /// <summary>Raised when an existing world is to be started.</summary>
    public event Action<Guid>? StartRequested;

    /// <summary>Raised when a new world is to be created; the seed is null for a random one.</summary>
    public event Action<ulong?>? CreateRequested;

    /// <summary>Raised by the back button.</summary>
    public event Action? BackPressed;

    /// <summary>Gets the selected world, or null.</summary>
    public Guid? SelectedWorld
    {
        get
        {
            var selected = GetNode<ItemList>("%WorldList").GetSelectedItems();
            return selected.Length == 1 && selected[0] >= 0 && selected[0] < _ids.Count ? _ids[selected[0]] : null;
        }
    }

    /// <summary>Gets who sees the hosted lobby.</summary>
    public UiLobbyVisibility Visibility => (UiLobbyVisibility)GetNode<OptionButton>("%VisibilityOption").Selected;

    /// <summary>Gets a value indicating whether the host wants a Steam lobby.</summary>
    public bool UseSteam => GetNode<CheckBox>("%EnableSteamCheck").ButtonPressed && !GetNode<CheckBox>("%EnableSteamCheck").Disabled;

    /// <inheritdoc />
    public override void _Ready()
    {
        var visibility = GetNode<OptionButton>("%VisibilityOption");
        visibility.AddItem("ui.visibility_private", (int)UiLobbyVisibility.Private);
        visibility.AddItem("ui.visibility_friends", (int)UiLobbyVisibility.FriendsOnly);
        visibility.AddItem("ui.visibility_public", (int)UiLobbyVisibility.Public);
        visibility.Select((int)UiLobbyVisibility.FriendsOnly);

        UiNodes.Bind(this, "%BackButton", () => BackPressed?.Invoke());
        UiNodes.Bind(this, "%StartButton", OnStart);
        UiNodes.Bind(this, "%NewWorldButton", () =>
        {
            GetNode<Control>("%NewWorldWindow").Visible = true;
            GetNode<Label>("%SeedError").Text = string.Empty;
            GetNode<LineEdit>("%SeedInput").GrabFocus();
        });
        UiNodes.Bind(this, "%CancelButton", () => GetNode<Control>("%NewWorldWindow").Visible = false);
        UiNodes.Bind(this, "%CreateButton", OnCreate);
        GetNode<ItemList>("%WorldList").ItemActivated += _ => OnStart();
        GetNode<ItemList>("%WorldList").ItemSelected += _ => RefreshButtons();
        GetNode<Control>("%NewWorldWindow").Visible = false;
    }

    /// <summary>Shows the panel for solo or host.</summary>
    /// <param name="host">True for host mode (lobby options visible).</param>
    /// <param name="steamAvailable">Whether a Steam lobby can be used.</param>
    public void Prepare(bool host, bool steamAvailable)
    {
        GetNode<Control>("%HostOptions").Visible = host;
        var steam = GetNode<CheckBox>("%EnableSteamCheck");
        steam.Disabled = !steamAvailable;
        steam.ButtonPressed = steamAvailable && host;
        GetNode<Label>("%SteamStatus").Text = steamAvailable ? string.Empty : "ui.steam_unavailable";
        GetNode<Control>("%NewWorldWindow").Visible = false;
    }

    /// <summary>Replaces the list.</summary>
    /// <param name="worlds">Entries, sorted.</param>
    /// <param name="truncated">Whether more worlds exist than the limit.</param>
    public void SetWorlds(IReadOnlyList<WorldListEntry> worlds, bool truncated)
    {
        ArgumentNullException.ThrowIfNull(worlds);
        var list = GetNode<ItemList>("%WorldList");
        list.Clear();
        _ids.Clear();
        foreach (var world in worlds)
        {
            var text = string.Create(
                CultureInfo.InvariantCulture,
                $"{world.WorldId.ToString("N")[..8]}   {world.LastWriteUtc.ToLocalTime().ToString("g", CultureInfo.InvariantCulture)}");
            var index = list.AddItem(world.DatabasePresent ? text : text + "   (" + Tr("ui.world_unavailable") + ")");
            list.SetItemDisabled(index, !world.DatabasePresent);
            _ids.Add(world.WorldId);
        }

        GetNode<Label>("%ListStatus").Text = truncated ? "ui.worlds_truncated" : worlds.Count == 0 ? "ui.no_worlds" : string.Empty;
        RefreshButtons();
    }

    /// <summary>Blocks every start while an operation runs (a double click starts one world only).</summary>
    /// <param name="busy">Whether an operation runs.</param>
    public void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        var selectable = SelectedWorld is { } id && !GetNode<ItemList>("%WorldList").IsItemDisabled(_ids.IndexOf(id));
        GetNode<BaseButton>("%StartButton").Disabled = _busy || !selectable;
        GetNode<BaseButton>("%NewWorldButton").Disabled = _busy;
        GetNode<BaseButton>("%CreateButton").Disabled = _busy;
    }

    private void OnStart()
    {
        if (!_busy && SelectedWorld is { } id)
        {
            StartRequested?.Invoke(id);
        }
    }

    private void OnCreate()
    {
        if (_busy)
        {
            return;
        }

        var text = GetNode<LineEdit>("%SeedInput").Text.Trim();
        ulong? seed = null;
        if (text.Length > 0)
        {
            if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                GetNode<Label>("%SeedError").Text = "ui.seed_invalid";
                return;
            }

            seed = parsed;
        }

        GetNode<Control>("%NewWorldWindow").Visible = false;
        CreateRequested?.Invoke(seed);
    }
}
