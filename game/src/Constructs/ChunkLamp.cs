using Godot;

using Horyzont.Constructs.Content;

namespace Horyzont.Game.Constructs;

/// <summary>A lit lamp of a chunk: where its light sits (centre of the cell, in the space of the chunk node) and how it shines.</summary>
/// <param name="LocalPosition">Centre of the lamp cell relative to the chunk node.</param>
/// <param name="Light">Range, colour and energy from the block content.</param>
public readonly record struct ChunkLamp(Vector3 LocalPosition, BlockLightDef Light);

/// <summary>A lit lamp in the Godot world: where its light sits and how it shines.</summary>
/// <param name="Position">Global position of the light (centre of the lamp cell).</param>
/// <param name="Light">Range, colour and energy from the block content.</param>
public readonly record struct LampInstance(Vector3 Position, BlockLightDef Light);
