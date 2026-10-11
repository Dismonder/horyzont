using Godot;

using Horyzont.Client.Navigation;
using Horyzont.Core.Ids;
using Horyzont.Core.World.Markers;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.UI.Map;

/// <summary>
/// The planet map (K5-27 decision 12): the hypsometric texture of a <see cref="PlanetMap"/> with the players and the markers of the
/// planet. A click on the surface asks for a new marker at that point (<c>R + h</c> in that direction), a click on a marker for its edit.
/// There is no fog of war.
/// </summary>
public sealed partial class PlanetMapPanel : Control
{
    private const float ClickDistance = 12f;

    private readonly List<MarkerRecord> _markers = [];
    private readonly List<NumVector3> _players = [];
    private PlanetMap? _map;
    private NumVector3? _own;

    /// <summary>Raised when the surface is clicked: the planet and the point in its frame.</summary>
    public event Action<PlanetId, NumVector3>? SurfaceClicked;

    /// <summary>Raised when a marker is clicked.</summary>
    public event Action<MarkerRecord>? MarkerClicked;

    /// <summary>Gets the number of markers drawn by the last draw (tests).</summary>
    public int DrawnMarkers { get; private set; }

    /// <summary>Gets the number of players drawn by the last draw (tests).</summary>
    public int DrawnPlayers { get; private set; }

    /// <summary>Gets the shown map.</summary>
    public PlanetMap? Map => _map;

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
    }

    /// <summary>Shows a planet.</summary>
    /// <param name="map">The map of the planet.</param>
    public void ShowMap(PlanetMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map;
        QueueRedraw();
    }

    /// <summary>Sets what is drawn on the map.</summary>
    /// <param name="markers">Markers of the shown planet.</param>
    /// <param name="players">Positions of the other players on the planet.</param>
    /// <param name="own">The own position in the planet frame, when on this planet.</param>
    public void SetContents(IEnumerable<MarkerRecord> markers, IEnumerable<NumVector3> players, NumVector3? own)
    {
        _markers.Clear();
        _markers.AddRange(markers);
        _players.Clear();
        _players.AddRange(players);
        _own = own;
        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_map is { IsReady: false } map && map.Poll())
        {
            QueueRedraw();
        }
    }

    /// <inheritdoc />
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } button || _map is not { IsReady: true } map)
        {
            return;
        }

        var area = MapRect();
        foreach (var marker in _markers)
        {
            if (ToScreen(marker.Position, area) is { } at && at.DistanceTo(button.Position) <= ClickDistance)
            {
                MarkerClicked?.Invoke(marker);
                return;
            }
        }

        if (!area.HasPoint(button.Position))
        {
            return;
        }

        var u = (float)((button.Position.X - area.Position.X) / area.Size.X);
        var v = (float)((button.Position.Y - area.Position.Y) / area.Size.Y);
        var direction = PlanetMapProjection.Direction(u, v);
        SurfaceClicked?.Invoke(map.Planet.Id, direction * (map.Planet.RadiusMeters + map.HeightAt(u, v)));
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        DrawnMarkers = 0;
        DrawnPlayers = 0;
        if (_map is null)
        {
            return;
        }

        var area = MapRect();
        if (_map.Texture is { } texture)
        {
            DrawTextureRect(texture, area, false);
        }
        else
        {
            DrawRect(area, new Color(0.1f, 0.1f, 0.1f));
            DrawString(ThemeDB.FallbackFont, area.GetCenter(), Tr(_map.Failure is null ? "MAP_SAMPLING" : "MAP_FAILED"), HorizontalAlignment.Center, -1f, 16);
            return;
        }

        foreach (var player in _players)
        {
            if (ToScreen(player, area) is { } at)
            {
                DrawCircle(at, 4f, Colors.White);
                DrawnPlayers++;
            }
        }

        if (_own is { } own && ToScreen(own, area) is { } ownAt)
        {
            DrawCircle(ownAt, 5f, new Color(0.3f, 1f, 0.4f));
        }

        foreach (var marker in _markers)
        {
            if (ToScreen(marker.Position, area) is { } at)
            {
                DrawColoredPolygon([at + new Vector2(0f, -8f), at + new Vector2(6f, 0f), at + new Vector2(0f, 8f), at + new Vector2(-6f, 0f)], MarkerColors.Of(marker.Color));
                DrawString(ThemeDB.FallbackFont, at + new Vector2(8f, 4f), marker.Name, HorizontalAlignment.Left, -1f, 12);
                DrawnMarkers++;
            }
        }
    }

    private Rect2 MapRect()
    {
        // The projection is 2:1; fit the largest such rectangle in the control.
        var width = Mathf.Min(Size.X, Size.Y * 2f);
        var size = new Vector2(width, width * 0.5f);
        return new Rect2((Size - size) * 0.5f, size);
    }

    private static Vector2? ToScreen(NumVector3 position, Rect2 area)
    {
        if (!PlanetMapProjection.TryFromPosition(position, out var u, out var v))
        {
            return null;
        }

        return area.Position + new Vector2(u * area.Size.X, v * area.Size.Y);
    }
}
