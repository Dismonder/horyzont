using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Blocks;

/// <summary>The hint for the block under the crosshair.</summary>
/// <param name="Kind">What the key does.</param>
/// <param name="Construct">Construct of the block.</param>
/// <param name="Cell">Cell of the block.</param>
public readonly record struct BlockPrompt(BlockPromptKind Kind, EntityId Construct, CellPos Cell);
