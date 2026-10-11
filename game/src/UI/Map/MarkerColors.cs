using Godot;

using Horyzont.Core.World.Markers;

namespace Horyzont.Game.UI.Map;

/// <summary>Display colours of the eight marker colours.</summary>
public static class MarkerColors
{
    private static readonly Color[] _colors =
    [
        new(1f, 1f, 1f), new(1f, 0.3f, 0.3f), new(0.3f, 0.9f, 0.4f), new(0.4f, 0.55f, 1f),
        new(1f, 0.9f, 0.3f), new(0.3f, 0.95f, 0.95f), new(1f, 0.4f, 0.9f), new(1f, 0.65f, 0.2f),
    ];

    /// <summary>Gets the colour for a marker colour.</summary>
    /// <param name="color">The marker colour.</param>
    /// <returns>The display colour; white for an undefined value.</returns>
    public static Color Of(MarkerColor color) => (int)color < _colors.Length ? _colors[(int)color] : _colors[0];
}
