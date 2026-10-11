using Godot;

using Horyzont.Client.Navigation;
using Horyzont.Core.Ids;

namespace Horyzont.Game.UI.Map;

/// <summary>
/// The map of the star system (K5-27 decision 12): an orthographic view of the XZ plane of the system frame with pan (drag) and zoom
/// (wheel). It only draws a <see cref="SystemMapModel"/>: bodies with the zone of influence (3 R), the sun direction, the players and
/// ships the client knows, the markers and the own position. A click on a body opens the planet map, a click on a marker selects it.
/// </summary>
public sealed partial class SystemMapPanel : Control
{
    private const float ClickDistance = 12f;
    private const float DragThreshold = 4f;
    private static readonly Color _bodyColor = new(0.55f, 0.75f, 0.95f);
    private static readonly Color _zoneColor = new(0.55f, 0.75f, 0.95f, 0.35f);
    private static readonly Color _entityColor = new(0.9f, 0.9f, 0.9f);
    private static readonly Color _ownColor = new(0.3f, 1f, 0.4f);
    private static readonly Color _sunColor = new(1f, 0.9f, 0.4f);

    private SystemMapModel? _model;
    private double _centerX;
    private double _centerZ;
    private double _metersPerPixel = 1000;
    private bool _fitted;
    private bool _dragging;
    private double _dragged;

    /// <summary>Raised when a body is clicked.</summary>
    public event Action<PlanetId>? BodyClicked;

    /// <summary>Raised when a marker is clicked.</summary>
    public event Action<MarkerId>? MarkerClicked;

    /// <summary>Gets the number of bodies drawn by the last draw (tests).</summary>
    public int DrawnBodies { get; private set; }

    /// <summary>Gets the number of players and ships drawn by the last draw (tests).</summary>
    public int DrawnEntities { get; private set; }

    /// <summary>Gets the number of markers drawn by the last draw (tests).</summary>
    public int DrawnMarkers { get; private set; }

    /// <summary>Gets a value indicating whether the sun arrow was drawn by the last draw (tests).</summary>
    public bool DrewSun { get; private set; }

    /// <summary>Gets the model being drawn.</summary>
    public SystemMapModel? Model => _model;

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
    }

    /// <summary>Sets the model to draw; the first model also fits the view.</summary>
    /// <param name="model">The model.</param>
    public void SetModel(SystemMapModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        if (!_fitted && model.Bodies.Count > 0)
        {
            Fit();
        }

        QueueRedraw();
    }

    /// <summary>Fits the view to the whole system.</summary>
    public void Fit()
    {
        if (_model is null || _model.Bodies.Count == 0)
        {
            return;
        }

        double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
        foreach (var body in _model.Bodies)
        {
            minX = Math.Min(minX, body.X - body.ZoneRadiusMeters);
            maxX = Math.Max(maxX, body.X + body.ZoneRadiusMeters);
            minZ = Math.Min(minZ, body.Z - body.ZoneRadiusMeters);
            maxZ = Math.Max(maxZ, body.Z + body.ZoneRadiusMeters);
        }

        _centerX = 0.5 * (minX + maxX);
        _centerZ = 0.5 * (minZ + maxZ);
        var size = Size.X > 1 && Size.Y > 1 ? Size : new Vector2(1280, 720);
        _metersPerPixel = Math.Max((maxX - minX) / size.X, (maxZ - minZ) / size.Y) * 1.1;
        _fitted = true;
    }

    /// <summary>Screen position of a point of the system frame.</summary>
    /// <param name="x">System frame X.</param>
    /// <param name="z">System frame Z.</param>
    /// <returns>The position in this control.</returns>
    public Vector2 ToScreen(double x, double z) =>
        (Size * 0.5f) + new Vector2((float)((x - _centerX) / _metersPerPixel), (float)((z - _centerZ) / _metersPerPixel));

    /// <inheritdoc />
    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _metersPerPixel = Math.Max(0.01, _metersPerPixel * 0.8);
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _metersPerPixel = Math.Min(1e7, _metersPerPixel * 1.25);
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed)
                {
                    _dragging = true;
                    _dragged = 0;
                }
                else
                {
                    _dragging = false;
                    if (_dragged < DragThreshold)
                    {
                        Click(button.Position);
                    }
                }

                break;
            case InputEventMouseMotion motion when _dragging:
                _dragged += motion.Relative.Length();
                _centerX -= motion.Relative.X * _metersPerPixel;
                _centerZ -= motion.Relative.Y * _metersPerPixel;
                QueueRedraw();
                break;
        }
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        DrawnBodies = 0;
        DrawnEntities = 0;
        DrawnMarkers = 0;
        DrewSun = false;
        if (_model is null)
        {
            return;
        }

        var font = ThemeDB.FallbackFont;
        foreach (var body in _model.Bodies)
        {
            var centre = ToScreen(body.X, body.Z);
            DrawArc(centre, (float)(body.ZoneRadiusMeters / _metersPerPixel), 0f, Mathf.Tau, 64, _zoneColor, 1f, true);
            DrawCircle(centre, Mathf.Max(3f, (float)(body.RadiusMeters / _metersPerPixel)), _bodyColor);
            DrawString(font, centre + new Vector2(8f, -8f), Tr(body.NameKey), HorizontalAlignment.Left, -1f, 14);
            DrawnBodies++;
        }

        // The sun arrow sits in the top-right corner and points along the XZ projection of the direction of the sun.
        var sun = new Vector2((float)_model.SunDirection.X, (float)_model.SunDirection.Z);
        if (sun.LengthSquared() > 1e-6f)
        {
            var origin = new Vector2(Size.X - 60f, 60f);
            var tip = origin + (sun.Normalized() * 32f);
            DrawCircle(origin, 4f, _sunColor);
            DrawLine(origin, tip, _sunColor, 3f, true);
            DrawCircle(tip, 6f, _sunColor);
            DrewSun = true;
        }

        foreach (var entity in _model.Entities)
        {
            var at = ToScreen(entity.X, entity.Z);
            DrawRect(new Rect2(at - new Vector2(3f, 3f), new Vector2(6f, 6f)), _entityColor);
            if (entity.Name is { } name)
            {
                DrawString(font, at + new Vector2(6f, -6f), name, HorizontalAlignment.Left, -1f, 12);
            }

            DrawnEntities++;
        }

        foreach (var marker in _model.Markers)
        {
            var at = ToScreen(marker.X, marker.Z);
            DrawColoredPolygon([at + new Vector2(0f, -8f), at + new Vector2(6f, 0f), at + new Vector2(0f, 8f), at + new Vector2(-6f, 0f)], MarkerColors.Of(marker.Color));
            DrawString(font, at + new Vector2(8f, 4f), marker.Name, HorizontalAlignment.Left, -1f, 12);
            DrawnMarkers++;
        }

        if (_model.OwnPosition is { } own)
        {
            var at = ToScreen(own.X, own.Z);
            DrawCircle(at, 5f, _ownColor);
            DrawString(font, at + new Vector2(8f, 14f), Tr("MAP_YOU"), HorizontalAlignment.Left, -1f, 12, _ownColor);
        }
    }

    private void Click(Vector2 at)
    {
        if (_model is null)
        {
            return;
        }

        foreach (var marker in _model.Markers)
        {
            if (ToScreen(marker.X, marker.Z).DistanceTo(at) <= ClickDistance)
            {
                MarkerClicked?.Invoke(marker.Id);
                return;
            }
        }

        foreach (var body in _model.Bodies)
        {
            var radius = Mathf.Max(ClickDistance, (float)(body.RadiusMeters / _metersPerPixel));
            if (ToScreen(body.X, body.Z).DistanceTo(at) <= radius)
            {
                BodyClicked?.Invoke(body.Planet);
                return;
            }
        }
    }
}
