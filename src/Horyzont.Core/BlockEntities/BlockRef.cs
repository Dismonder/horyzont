using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.BlockEntities;

/// <summary>Key of a block with state (K6-30): a construct and one of its cells.</summary>
/// <param name="Construct">Construct.</param>
/// <param name="Cell">Cell of the block.</param>
public readonly record struct BlockRef(EntityId Construct, CellPos Cell);
