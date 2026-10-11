using System.Numerics;

namespace Horyzont.Client.Navigation;

/// <summary>
/// Where the navigation target appears on the screen (K5-27 decision 11). Works on the direction to the target in the camera space
/// (x right, y up, the camera looks along −z), so the HUD never projects a point 100 km from the camera: the direction is enough.
/// A target in the view gets a pin at its projection; a target behind the camera or outside the view gets an arrow on the screen
/// edge, kept inside a margin. Single precision is fine here: it is the final direction, not a world position. No allocation.
/// </summary>
public static class NavigationScreenMath
{
    /// <summary>Margin between the edge arrow and the border of the viewport, pixels.</summary>
    public const float EdgeMargin = 32f;

    /// <summary>Places the target on the screen.</summary>
    /// <param name="cameraDirection">Direction to the target in camera space; need not be normalized.</param>
    /// <param name="viewport">Viewport size in pixels.</param>
    /// <param name="verticalFov">Vertical field of view, radians.</param>
    /// <param name="screen">Receives the pin position or the arrow position, pixels from the top-left corner.</param>
    /// <param name="arrowAngle">Receives the angle of the arrow (radians, 0 points right, y down); meaningful when the result is false.</param>
    /// <param name="margin">Margin of the edge arrow, pixels.</param>
    /// <returns>True when the target is in the view (a pin); false when it needs an edge arrow.</returns>
    public static bool TryPlace(Vector3 cameraDirection, Vector2 viewport, float verticalFov, out Vector2 screen, out float arrowAngle, float margin = EdgeMargin)
    {
        var center = viewport * 0.5f;
        if (cameraDirection.Z < -1e-6f)
        {
            var tan = MathF.Tan(verticalFov * 0.5f);
            var scale = center.Y / tan;
            var x = center.X + (cameraDirection.X / -cameraDirection.Z * scale);
            var y = center.Y - (cameraDirection.Y / -cameraDirection.Z * scale);
            if (x >= margin && x <= viewport.X - margin && y >= margin && y <= viewport.Y - margin)
            {
                screen = new Vector2(x, y);
                arrowAngle = 0f;
                return true;
            }
        }

        var dx = cameraDirection.X;
        var dy = -cameraDirection.Y;
        if (MathF.Abs(dx) < 1e-6f && MathF.Abs(dy) < 1e-6f)
        {
            // Straight behind: park the arrow at the bottom, pointing down.
            dx = 0f;
            dy = 1f;
        }

        var tx = MathF.Abs(dx) > 1e-6f ? (center.X - margin) / MathF.Abs(dx) : float.PositiveInfinity;
        var ty = MathF.Abs(dy) > 1e-6f ? (center.Y - margin) / MathF.Abs(dy) : float.PositiveInfinity;
        var t = MathF.Min(tx, ty);
        screen = new Vector2(center.X + (dx * t), center.Y + (dy * t));
        arrowAngle = MathF.Atan2(dy, dx);
        return false;
    }
}
