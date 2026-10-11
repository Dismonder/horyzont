using Godot;

using Horyzont.Client.Markers;
using Horyzont.Client.Navigation;
using Horyzont.Game.Frames;

using NumVector2 = System.Numerics.Vector2;
using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Client.Navigation;

/// <summary>
/// The navigation target on the screen (K5-27 decision 11): a pin with the name and the distance when the target is in the view, an
/// <see cref="EdgeArrow"/> on the screen edge (32 px margin) when it is behind the camera or outside the view. The direction comes from
/// <see cref="NavigationCoordinator"/> in double precision; only the final unit direction is converted to the camera space. The label
/// text is rebuilt in a reused buffer at most ten times a second and the control is set only when the text changed.
/// </summary>
public sealed partial class NavigationHud : CanvasLayer
{
    private const double TextRefreshSeconds = 0.1;
    private const int BufferSize = 96;

    private readonly NavigationCoordinator _coordinator;
    private readonly ClientMarkerStore _markers;
    private readonly Func<double> _renderTick;
    private readonly char _decimalSeparator;
    private readonly char[] _text = new char[BufferSize];
    private readonly char[] _shown = new char[BufferSize];
    private int _shownLength = -1;
    private double _sinceText = TextRefreshSeconds;
    private Label? _pin;
    private EdgeArrow? _arrow;

    /// <summary>Creates the HUD.</summary>
    /// <param name="coordinator">The navigation target and guidance.</param>
    /// <param name="markers">Markers, for the name of a marker target.</param>
    /// <param name="renderTick">Server tick being rendered.</param>
    /// <param name="decimalSeparator">Decimal separator of the interface language.</param>
    public NavigationHud(NavigationCoordinator coordinator, ClientMarkerStore markers, Func<double> renderTick, char decimalSeparator = '.')
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(renderTick);
        _coordinator = coordinator;
        _markers = markers;
        _renderTick = renderTick;
        _decimalSeparator = decimalSeparator;
        Name = "NavigationHud";
        Layer = 8;
    }

    /// <summary>Gets a value indicating whether the pin is shown (tests).</summary>
    public bool PinVisible => _pin?.Visible == true;

    /// <summary>Gets a value indicating whether the edge arrow is shown (tests).</summary>
    public bool ArrowVisible => _arrow?.Visible == true;

    /// <summary>Gets the screen position of the pin or of the arrow (tests).</summary>
    public Vector2 ScreenPosition => _pin?.Visible == true ? _pin.Position : _arrow?.Position ?? default;

    /// <summary>Gets the text of the pin label (tests).</summary>
    public string PinText => _pin?.Text ?? string.Empty;

    /// <inheritdoc />
    public override void _Ready()
    {
        _pin = new Label { Name = "Pin", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _pin.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.2f));
        _pin.AddThemeColorOverride("font_outline_color", Colors.Black);
        _pin.AddThemeConstantOverride("outline_size", 4);
        _arrow = new EdgeArrow { Name = "EdgeArrow", Visible = false };
        AddChild(_pin);
        AddChild(_arrow);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_pin is null || _arrow is null)
        {
            return;
        }

        if (GetViewport().GetCamera3D() is not { } camera || !_coordinator.TryGetGuidance(_renderTick(), out var guidance) || guidance.DistanceMeters < 1e-6)
        {
            _pin.Visible = false;
            _arrow.Visible = false;
            _shownLength = -1;
            return;
        }

        var direction = GodotFrames.ToGodot(guidance.WorldDelta / guidance.DistanceMeters);
        var inCamera = camera.GlobalTransform.Basis.Inverse() * direction;
        var view = GetViewport().GetVisibleRect().Size;
        var inView = NavigationScreenMath.TryPlace(
            new NumVector3((float)inCamera.X, (float)inCamera.Y, (float)inCamera.Z), new NumVector2((float)view.X, (float)view.Y), (float)Mathf.DegToRad(camera.Fov),
            out var screen, out var angle);

        _sinceText += delta;
        if (_sinceText >= TextRefreshSeconds)
        {
            _sinceText = 0;
            RefreshText(guidance);
        }

        _pin.Visible = inView;
        _arrow.Visible = !inView;
        if (inView)
        {
            _pin.Position = new Vector2(screen.X, screen.Y);
        }
        else
        {
            _arrow.Position = new Vector2(screen.X - 12f, screen.Y - 12f);
            _arrow.Angle = angle;
        }
    }

    private void RefreshText(in NavigationGuidance guidance)
    {
        var length = 0;
        if (guidance.Target.Kind == NavigationTargetKind.Marker && _markers.TryGet(new Horyzont.Core.Ids.MarkerId(guidance.Target.Id), out var marker))
        {
            length = marker.Name.AsSpan().TryCopyTo(_text) ? marker.Name.Length : 0;
            if (length > 0 && length < BufferSize - 1)
            {
                _text[length++] = ' ';
            }
        }

        if (!DistanceFormatter.TryFormat(guidance.DistanceMeters, _text.AsSpan(length), out var written, _decimalSeparator))
        {
            written = 0;
        }

        length += written;
        if (length == _shownLength && _text.AsSpan(0, length).SequenceEqual(_shown.AsSpan(0, length)))
        {
            return;
        }

        _text.AsSpan(0, length).CopyTo(_shown);
        _shownLength = length;
        _pin!.Text = new string(_shown, 0, length);
    }
}
