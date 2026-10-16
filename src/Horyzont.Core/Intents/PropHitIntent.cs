using Horyzont.Core.Ids;

namespace Horyzont.Core.Intents;

/// <summary>
/// Hit a prop of the planet generator (a tree, a bush, a rock) with the item in a hotbar slot (intent 27, spec 03, K6-31 D24). The last
/// hit harvests the prop; earlier hits only wear it down on the server.
/// </summary>
/// <param name="Planet">Planet of the prop.</param>
/// <param name="Prop">The prop.</param>
/// <param name="ToolSlot">Hotbar slot of the tool (below 256); an empty slot or an item that is not a tool is the bare hand.</param>
public sealed record PropHitIntent(PlanetId Planet, PropId Prop, ushort ToolSlot) : Intent;
