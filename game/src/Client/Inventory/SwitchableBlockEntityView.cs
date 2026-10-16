using System.Diagnostics.CodeAnalysis;

using Horyzont.Client.Blocks;
using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Game.Client.Inventory;

/// <summary>
/// The view of the block states that the station session reads (K6-32). The inventory node is built before the composition knows where the
/// states come from (the world shared with the server, or the replication of a remote client), so the source is set later; until then no
/// block has a state.
/// </summary>
internal sealed class SwitchableBlockEntityView : IBlockEntityView
{
    /// <summary>Gets or sets the real source; null until the composition sets it.</summary>
    public IBlockEntityView? Inner { get; set; }

    /// <inheritdoc />
    public bool TryGet(EntityId construct, CellPos cell, [NotNullWhen(true)] out BlockEntityState? state)
    {
        if (Inner is { } inner)
        {
            return inner.TryGet(construct, cell, out state);
        }

        state = null;
        return false;
    }
}
