using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Blocks;

/// <summary>Read-only view of the states of the functional blocks that the local player may look at (K6-30).</summary>
public interface IBlockEntityView
{
    /// <summary>Looks up the state of one block.</summary>
    /// <param name="construct">Construct.</param>
    /// <param name="cell">Cell of the block.</param>
    /// <param name="state">The state when the block has one.</param>
    /// <returns>True when found.</returns>
    bool TryGet(EntityId construct, CellPos cell, [NotNullWhen(true)] out BlockEntityState? state);
}
