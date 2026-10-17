using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Checkpoints;

/// <summary>The state of a functional block in a checkpoint (blob <c>BlockEntity</c>, 21).</summary>
/// <param name="Construct">Construct.</param>
/// <param name="Cell">Cell of the block.</param>
/// <param name="State">State of the block.</param>
public sealed record BlockEntityRecord(EntityId Construct, CellPos Cell, BlockEntityState State);
