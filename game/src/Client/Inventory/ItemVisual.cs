using Godot;

using Horyzont.Client.Tools;

namespace Horyzont.Game.Client.Inventory;

/// <summary>How an item looks on a slot (the <c>visual</c> object of <c>items.json</c>).</summary>
/// <param name="Glyph">One or two letters drawn on the hexagon of a resource.</param>
/// <param name="Color">Fill colour of the hexagon, or the main colour of a tool drawing.</param>
/// <param name="Tool">The tool the item selects from the hotbar (<c>clientTool</c>); <see cref="ToolKind.None"/> for resources.</param>
public readonly record struct ItemVisual(string Glyph, Color Color, ToolKind Tool);
