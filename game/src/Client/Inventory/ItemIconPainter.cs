using Godot;

using Horyzont.Client.Tools;

namespace Horyzont.Game.Client.Inventory;

/// <summary>
/// Draws item icons with <c>_Draw</c> primitives (K6-29W D17): a glyph on a coloured hexagon for resources, a small drawing per tool.
/// No textures, so the look comes from code and the colours of <c>items.json</c>.
/// </summary>
public static class ItemIconPainter
{
    /// <summary>Draws an icon.</summary>
    /// <param name="canvas">The item that is drawing (call from its <c>_Draw</c>).</param>
    /// <param name="rect">Where the icon goes.</param>
    /// <param name="visual">The visual of the item.</param>
    /// <param name="alpha">Opacity, 1 for a normal icon.</param>
    public static void Draw(CanvasItem canvas, Rect2 rect, ItemVisual visual, float alpha = 1f)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        switch (visual.Tool)
        {
            case ToolKind.TerrainManipulator:
                DrawManipulator(canvas, rect, visual.Color, alpha);
                break;
            case ToolKind.Builder:
                DrawBuilder(canvas, rect, visual.Color, alpha);
                break;
            default:
                DrawResource(canvas, rect, visual, alpha);
                break;
        }
    }

    private static Color With(Color color, float alpha) => new(color.R, color.G, color.B, color.A * alpha);

    private static void DrawResource(CanvasItem canvas, Rect2 rect, ItemVisual visual, float alpha)
    {
        var center = rect.GetCenter();
        var radius = Math.Min(rect.Size.X, rect.Size.Y) * 0.5f;
        var outer = Hexagon(center, radius);
        var inner = Hexagon(center, radius * 0.78f);
        canvas.DrawColoredPolygon(outer, With(visual.Color.Darkened(0.35f), alpha));
        canvas.DrawColoredPolygon(inner, With(visual.Color, alpha));
        var closed = new Vector2[outer.Length + 1];
        outer.CopyTo(closed, 0);
        closed[^1] = outer[0];
        canvas.DrawPolyline(closed, With(visual.Color.Lightened(0.4f), alpha), 1.5f, true);
        var font = ThemeDB.FallbackFont;
        var size = (int)(radius * 0.8f);
        var width = font.GetStringSize(visual.Glyph, HorizontalAlignment.Left, -1, size).X;
        var baseline = center + new Vector2(-width / 2f, (font.GetAscent(size) - font.GetDescent(size)) / 2f);
        canvas.DrawString(font, baseline + new Vector2(1f, 1f), visual.Glyph, HorizontalAlignment.Left, -1, size, new Color(0f, 0f, 0f, 0.55f * alpha));
        canvas.DrawString(font, baseline, visual.Glyph, HorizontalAlignment.Left, -1, size, With(HudTheme.Text, alpha));
    }

    /// <summary>A hand device: a grip, a barrel and a glowing emitter with a scanning ring.</summary>
    private static void DrawManipulator(CanvasItem canvas, Rect2 rect, Color color, float alpha)
    {
        var s = Math.Min(rect.Size.X, rect.Size.Y);
        var o = rect.GetCenter() - new Vector2(s / 2f, s / 2f);
        Vector2 P(float x, float y) => o + new Vector2(x * s, y * s);

        var body = With(new Color(0.55f, 0.62f, 0.68f), alpha);
        var shade = With(new Color(0.28f, 0.34f, 0.4f), alpha);
        canvas.DrawColoredPolygon([P(0.14f, 0.92f), P(0.3f, 0.92f), P(0.4f, 0.55f), P(0.24f, 0.5f)], shade); // grip
        canvas.DrawColoredPolygon([P(0.2f, 0.42f), P(0.62f, 0.3f), P(0.7f, 0.48f), P(0.28f, 0.6f)], body); // body
        canvas.DrawColoredPolygon([P(0.6f, 0.3f), P(0.84f, 0.22f), P(0.88f, 0.34f), P(0.68f, 0.46f)], shade); // barrel
        var glow = With(color, alpha);
        canvas.DrawCircle(P(0.88f, 0.26f), s * 0.1f, glow);
        canvas.DrawArc(P(0.88f, 0.26f), s * 0.17f, 0f, Mathf.Tau, 20, With(color.Lightened(0.3f), alpha * 0.8f), 1.5f, true);
        canvas.DrawLine(P(0.3f, 0.5f), P(0.58f, 0.42f), glow, 2f, true);
    }

    /// <summary>An isometric cube with three shaded faces.</summary>
    private static void DrawBuilder(CanvasItem canvas, Rect2 rect, Color color, float alpha)
    {
        var s = Math.Min(rect.Size.X, rect.Size.Y);
        var c = rect.GetCenter();
        var r = s * 0.46f;
        var top = c + new Vector2(0f, -r);
        var upperRight = c + new Vector2(r * 0.87f, -r * 0.5f);
        var lowerRight = c + new Vector2(r * 0.87f, r * 0.5f);
        var bottom = c + new Vector2(0f, r);
        var lowerLeft = c + new Vector2(-r * 0.87f, r * 0.5f);
        var upperLeft = c + new Vector2(-r * 0.87f, -r * 0.5f);
        canvas.DrawColoredPolygon([top, upperRight, c, upperLeft], With(color.Lightened(0.25f), alpha));
        canvas.DrawColoredPolygon([upperLeft, c, bottom, lowerLeft], With(color.Darkened(0.2f), alpha));
        canvas.DrawColoredPolygon([c, upperRight, lowerRight, bottom], With(color.Darkened(0.45f), alpha));
        var edge = With(HudTheme.Text, alpha * 0.75f);
        canvas.DrawPolyline([top, upperRight, lowerRight, bottom, lowerLeft, upperLeft, top], edge, 1.5f, true);
        canvas.DrawLine(c, upperLeft, edge, 1f, true);
        canvas.DrawLine(c, upperRight, edge, 1f, true);
        canvas.DrawLine(c, bottom, edge, 1f, true);
    }

    private static Vector2[] Hexagon(Vector2 center, double radius)
    {
        var points = new Vector2[6];
        for (var i = 0; i < 6; i++)
        {
            var angle = Mathf.DegToRad((60f * i) - 30f);
            points[i] = center + new Vector2(Math.Cos(angle), Math.Sin(angle)) * radius;
        }

        return points;
    }
}
