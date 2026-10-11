using Godot;

namespace Horyzont.Game.Client.Navigation;

/// <summary>A small arrow at the screen edge that points to the navigation target when it is behind the camera or outside the view (K5-27).</summary>
public sealed partial class EdgeArrow : Control
{
    private static readonly Color _color = new(1f, 0.85f, 0.2f);
    private float _angle;

    /// <summary>Gets the angle of the arrow in radians; 0 points right, the y axis points down.</summary>
    public float Angle
    {
        get => _angle;
        set
        {
            if (!Mathf.IsEqualApprox(_angle, value))
            {
                _angle = value;
                QueueRedraw();
            }
        }
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(24f, 24f);
        Size = new Vector2(24f, 24f);
        PivotOffset = new Vector2(12f, 12f);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        var centre = new Vector2(12f, 12f);
        var cos = Mathf.Cos(_angle);
        var sin = Mathf.Sin(_angle);
        Vector2 Rotate(float x, float y) => centre + new Vector2((x * cos) - (y * sin), (x * sin) + (y * cos));
        DrawColoredPolygon([Rotate(11f, 0f), Rotate(-8f, -8f), Rotate(-4f, 0f), Rotate(-8f, 8f)], _color);
    }
}
