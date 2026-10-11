using Godot;

using Horyzont.Client.UI;

namespace Horyzont.Game.UI;

/// <summary>Join by address, port and password, or through a Steam friend; the password lives only in the form.</summary>
public sealed partial class JoinPanel : Control
{
    private readonly List<ulong> _lobbies = [];
    private bool _busy;
    private bool _steamMode;

    /// <summary>Raised with validated host, port and password.</summary>
    public event Action<string, int, string>? ConnectRequested;

    /// <summary>Raised when the Steam-only form is confirmed (the password of the lobby host).</summary>
    public event Action<string>? SteamPasswordEntered;

    /// <summary>Raised when a friend's lobby is to be joined.</summary>
    public event Action<ulong>? SteamJoinRequested;

    /// <summary>Raised by the back button.</summary>
    public event Action? BackPressed;

    /// <inheritdoc />
    public override void _Ready()
    {
        UiNodes.Bind(this, "%BackButton", () =>
        {
            ClearSecrets();
            BackPressed?.Invoke();
        });
        UiNodes.Bind(this, "%ConnectButton", OnConnect);
        UiNodes.Bind(this, "%SteamJoinButton", () =>
        {
            var selected = GetNode<ItemList>("%FriendsList").GetSelectedItems();
            if (!_busy && selected.Length == 1 && selected[0] < _lobbies.Count)
            {
                SteamJoinRequested?.Invoke(_lobbies[selected[0]]);
            }
        });
        GetNode<LineEdit>("%PortInput").Text = "27015";
        GetNode<LineEdit>("%PasswordInput").Secret = true;
    }

    /// <summary>Resets the form for a new visit.</summary>
    /// <param name="steamAvailable">Whether the friends list and the Steam button are usable.</param>
    public void Prepare(bool steamAvailable)
    {
        _steamMode = false;
        GetNode<Control>("%AddressSection").Visible = true;
        GetNode<Control>("%SteamSection").Visible = true;
        GetNode<BaseButton>("%SteamJoinButton").Disabled = !steamAvailable;
        GetNode<Label>("%FormStatus").Text = steamAvailable ? string.Empty : "ui.steam_unavailable";
        ClearSecrets();
    }

    /// <summary>Shows only the password and connect controls (a lobby host requires a password).</summary>
    public void PrepareSteamPassword()
    {
        _steamMode = true;
        GetNode<Control>("%AddressSection").Visible = false;
        GetNode<Control>("%SteamSection").Visible = false;
        ClearSecrets();
        GetNode<LineEdit>("%PasswordInput").GrabFocus();
    }

    /// <summary>Replaces the friends list.</summary>
    /// <param name="friends">Friends that are in a lobby.</param>
    public void SetFriends(ReadOnlySpan<UiLobbyFriend> friends)
    {
        var list = GetNode<ItemList>("%FriendsList");
        list.Clear();
        _lobbies.Clear();
        foreach (var friend in friends)
        {
            if (friend.LobbyId != 0)
            {
                list.AddItem(friend.Name);
                _lobbies.Add(friend.LobbyId);
            }
        }
    }

    /// <summary>Blocks every connect while an operation runs.</summary>
    /// <param name="busy">Whether an operation runs.</param>
    public void SetBusy(bool busy)
    {
        _busy = busy;
        GetNode<BaseButton>("%ConnectButton").Disabled = busy;
    }

    private void OnConnect()
    {
        if (_busy)
        {
            return;
        }

        var password = GetNode<LineEdit>("%PasswordInput").Text;
        var status = GetNode<Label>("%FormStatus");
        if (!JoinAddressParser.IsPasswordValid(password))
        {
            status.Text = "ui.join_invalid_password";
            return;
        }

        if (_steamMode)
        {
            SteamPasswordEntered?.Invoke(password);
            ClearSecrets();
            return;
        }

        var hostText = GetNode<LineEdit>("%HostInput").Text;
        var portText = GetNode<LineEdit>("%PortInput").Text;
        if (!JoinAddressParser.TryParse(hostText, "1", out _, out _))
        {
            status.Text = "ui.join_invalid_host";
            return;
        }

        if (!JoinAddressParser.TryParse(hostText, portText, out var host, out var port))
        {
            status.Text = "ui.join_invalid_port";
            return;
        }

        status.Text = string.Empty;
        ConnectRequested?.Invoke(host, port, password);
        ClearSecrets();
    }

    private void ClearSecrets() => GetNode<LineEdit>("%PasswordInput").Text = string.Empty;
}
