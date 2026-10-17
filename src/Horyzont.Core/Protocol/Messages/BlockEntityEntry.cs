using Horyzont.Core.BlockEntities;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>The state of one functional block in a <see cref="BlockEntityStates"/> message.</summary>
/// <param name="Cell">Cell of the block.</param>
/// <param name="State">State of the block.</param>
public sealed record BlockEntityEntry(CellPos Cell, BlockEntityState State);
