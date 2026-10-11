using System.Text;

using Godot;

using Horyzont.Client.Markers;
using Horyzont.Client.Navigation;
using Horyzont.Client.Session;
using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Markers;
using Horyzont.Game.Client.Tools;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.UI.Map;

/// <summary>
/// Creates, edits and deletes a marker (K5-27 decision 12): the name with a byte counter (at most 32 UTF-8 bytes), one of eight icons
/// and eight colours, the shared switch (creation only) and the buttons. It sends <see cref="MarkerSetIntent"/> and
/// <see cref="MarkerRemoveIntent"/> through <see cref="IIntentSender"/> and closes when the server accepts; a refusal is shown under
/// the key <c>REJECT_*</c>, for the limit <c>REJECT_MARKER_LIMIT</c>.
/// </summary>
public sealed partial class MarkerEditDialog : PanelContainer, IClientSessionListener
{
    private readonly IIntentSender _sender;
    private readonly ClientMarkerStore _markers;
    private readonly NavigationCoordinator _navigation;
    private MarkerRecord? _editing;
    private PlanetId _planet;
    private NumVector3 _position;
    private uint _pendingSeq;
    private bool _hasPending;
    private Label _title = null!;
    private LineEdit _name = null!;
    private Label _bytes = null!;
    private OptionButton _icon = null!;
    private OptionButton _color = null!;
    private CheckBox _shared = null!;
    private Label _error = null!;
    private Button _save = null!;
    private Button _delete = null!;
    private Button _navigate = null!;

    /// <summary>Creates the dialog.</summary>
    /// <param name="sender">Where the intents go.</param>
    /// <param name="markers">Markers of the client (the edited marker is read from it).</param>
    /// <param name="navigation">The navigation target selected by the navigate button.</param>
    public MarkerEditDialog(IIntentSender sender, ClientMarkerStore markers, NavigationCoordinator navigation)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(navigation);
        _sender = sender;
        _markers = markers;
        _navigation = navigation;
        Name = "MarkerEditDialog";
        Visible = false;
    }

    /// <summary>Raised when the dialog closed.</summary>
    public event Action? Closed;

    /// <summary>Gets the text of the error line (tests).</summary>
    public string ErrorText => _error?.Text ?? string.Empty;

    /// <summary>Gets the marker being edited, or null when a new one is created (tests).</summary>
    public MarkerRecord? Editing => _editing;

    /// <inheritdoc />
    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(320f, 0f);
        SetAnchorsPreset(Control.LayoutPreset.Center);
        var box = new VBoxContainer();
        AddChild(box);
        _title = new Label();
        _name = new LineEdit { PlaceholderText = "MARKER_NAME" };
        _bytes = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        _icon = new OptionButton();
        _color = new OptionButton();
        for (var i = 0; i <= (int)MarkerIcon.Poi; i++)
        {
            _icon.AddItem("MARKER_ICON_" + i, i);
            _color.AddItem("MARKER_COLOR_" + i, i);
        }

        _shared = new CheckBox { Text = "MARKER_SHARED" };
        _error = new Label();
        _error.AddThemeColorOverride("font_color", Colors.OrangeRed);
        _save = new Button { Text = "MARKER_SAVE" };
        _delete = new Button { Text = "MARKER_DELETE" };
        _navigate = new Button { Text = "MARKER_NAVIGATE" };
        var cancel = new Button { Text = "MARKER_CANCEL" };
        var buttons = new HBoxContainer();
        foreach (var button in new[] { _save, _delete, _navigate, cancel })
        {
            buttons.AddChild(button);
        }

        foreach (var control in new Control[] { _title, _name, _bytes, _icon, _color, _shared, _error, buttons })
        {
            box.AddChild(control);
        }

        _name.TextChanged += _ => UpdateBytes();
        _save.Pressed += Save;
        _delete.Pressed += Delete;
        _navigate.Pressed += Navigate;
        cancel.Pressed += Close;
    }

    /// <summary>Opens the dialog for a new marker.</summary>
    /// <param name="planet">Planet whose frame holds the position.</param>
    /// <param name="position">Position in the planet frame.</param>
    public void OpenNew(PlanetId planet, NumVector3 position)
    {
        _editing = null;
        _planet = planet;
        _position = position;
        Present("MARKER_NEW", string.Empty, 0, 0, shared: false, isEdit: false);
    }

    /// <summary>Opens the dialog for an existing marker.</summary>
    /// <param name="marker">The marker; edit and delete go through the server, which refuses foreign private markers.</param>
    public void OpenEdit(MarkerRecord marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        _editing = marker;
        _planet = marker.Planet;
        _position = marker.Position;
        Present("MARKER_EDIT", marker.Name, (int)marker.Icon, (int)marker.Color, marker.IsShared, isEdit: true);
    }

    /// <summary>Closes the dialog.</summary>
    public void Close()
    {
        _hasPending = false;
        Visible = false;
        Closed?.Invoke();
    }

    /// <inheritdoc />
    public void OnIntentReply(IntentReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (!_hasPending || reply.ClientSeq != _pendingSeq)
        {
            return;
        }

        if (reply.Reason == RejectReason.None)
        {
            Close();
            return;
        }

        _hasPending = false;
        _error.Text = ToolHud.RejectKey(reply.Reason);
        SetButtonsEnabled(true);
    }

    private static int ByteCount(string text) => Encoding.UTF8.GetByteCount(text);

    private void Present(string titleKey, string name, int icon, int color, bool shared, bool isEdit)
    {
        _title.Text = titleKey;
        _name.Text = name;
        _icon.Select(icon);
        _color.Select(color);
        _shared.ButtonPressed = shared;
        _shared.Visible = !isEdit;
        _delete.Visible = isEdit;
        _navigate.Visible = isEdit;
        _error.Text = string.Empty;
        _hasPending = false;
        SetButtonsEnabled(true);
        UpdateBytes();
        Visible = true;
        _name.GrabFocus();
    }

    private void UpdateBytes()
    {
        var count = ByteCount(_name.Text);
        _bytes.Text = count + "/" + MarkerRules.MaxNameBytes;
        _save.Disabled = !MarkerRules.IsValidName(_name.Text);
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _save.Disabled = !enabled || !MarkerRules.IsValidName(_name.Text);
        _delete.Disabled = !enabled;
    }

    private void Save()
    {
        if (!_sender.CanSend || !MarkerRules.IsValidName(_name.Text))
        {
            return;
        }

        var marker = _editing?.Id ?? MarkerId.None;
        var shared = _editing?.IsShared ?? _shared.ButtonPressed;
        Send(new MarkerSetIntent(marker, shared, _planet, _position, _name.Text, (MarkerIcon)_icon.Selected, (MarkerColor)_color.Selected));
    }

    private void Delete()
    {
        if (_sender.CanSend && _editing is { } marker)
        {
            Send(new MarkerRemoveIntent(marker.Id));
        }
    }

    private void Navigate()
    {
        if (_editing is { } marker && _markers.TryGet(marker.Id, out _))
        {
            _navigation.Select(NavigationTarget.ForMarker(marker.Id));
            Close();
        }
    }

    private void Send(Intent intent)
    {
        _pendingSeq = _sender.Submit(intent);
        _hasPending = true;
        _error.Text = string.Empty;
        SetButtonsEnabled(false);
    }
}
