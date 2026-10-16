using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Constructs.Sources;

/// <summary>Translates applied operations into the chunks and constructs whose view must be refreshed.</summary>
public static class ConstructDirtyChunks
{
    /// <summary>
    /// Chunks to remesh after an operation: the chunk of the cell plus each neighbour chunk the cell touches (cell on a chunk
    /// face; edge and corner cells touch two and three faces). Constructs created or removed by the operation are reported
    /// separately; a construct that was removed produces no dirty chunks.
    /// </summary>
    /// <param name="operation">The applied operation.</param>
    /// <param name="changes">Its <see cref="ConstructChange"/> entries.</param>
    /// <param name="dirty">Receives chunks to rebuild.</param>
    /// <param name="created">Receives constructs that were created (all their chunks must be built).</param>
    /// <param name="removed">Receives constructs that were removed.</param>
    /// <param name="reposed">Receives entities that were reparented (a construct pose refresh, no mesh work).</param>
    public static void Collect(
        Operation operation, IReadOnlyList<ConstructChange> changes, List<ConstructChunkKey> dirty, List<EntityId> created,
        List<EntityId> removed, List<EntityId>? reposed = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(dirty);
        ArgumentNullException.ThrowIfNull(created);
        ArgumentNullException.ThrowIfNull(removed);
        foreach (var change in changes)
        {
            if (change.Removed && !removed.Contains(change.Construct))
            {
                removed.Add(change.Construct);
            }
        }

        // Item components of a compound move nothing in a construct: only its construct part counts (OperationConstructs, K6-30 D14).
        var part = operation;
        if (operation is CompoundOp)
        {
            if (!OperationConstructs.TryGetConstructPart(operation, out var constructPart))
            {
                return;
            }

            part = constructPart;
        }

        switch (part)
        {
            case ConstructCreateOp create:
                created.Add(create.Construct);
                break;
            case BlockSetOp set:
                AddCell(set.Construct, set.Cell, removed, dirty);
                break;
            case BlockClearOp clear:
                AddCell(clear.Construct, clear.Cell, removed, dirty);
                break;
            case BlockFunctionStateSetOp state:
                AddCell(state.Construct, state.Cell, removed, dirty);
                break;
            case CompoundOp states:
                // Only function states are left in the part of a compound without a construct edit (a door column).
                foreach (var component in states.Components)
                {
                    if (component is BlockFunctionStateSetOp columnState)
                    {
                        AddCell(columnState.Construct, columnState.Cell, removed, dirty);
                    }
                }

                break;
            case EntityReparentOp reparent:
                reposed?.Add(reparent.Entity);
                break;
            case ShipModeSetOp shipMode:
                reposed?.Add(shipMode.Construct);
                break;
            default:
                break;
        }
    }

    private static void AddCell(EntityId construct, CellPos cell, List<EntityId> removed, List<ConstructChunkKey> dirty)
    {
        if (removed.Contains(construct))
        {
            return;
        }

        var chunk = cell.Chunk;
        dirty.Add(new ConstructChunkKey(construct, chunk));
        var x = cell.X & 15;
        var y = cell.Y & 15;
        var z = cell.Z & 15;
        if (x == 0)
        {
            dirty.Add(new ConstructChunkKey(construct, chunk with { X = chunk.X - 1 }));
        }
        else if (x == 15)
        {
            dirty.Add(new ConstructChunkKey(construct, chunk with { X = chunk.X + 1 }));
        }

        if (y == 0)
        {
            dirty.Add(new ConstructChunkKey(construct, chunk with { Y = chunk.Y - 1 }));
        }
        else if (y == 15)
        {
            dirty.Add(new ConstructChunkKey(construct, chunk with { Y = chunk.Y + 1 }));
        }

        if (z == 0)
        {
            dirty.Add(new ConstructChunkKey(construct, chunk with { Z = chunk.Z - 1 }));
        }
        else if (z == 15)
        {
            dirty.Add(new ConstructChunkKey(construct, chunk with { Z = chunk.Z + 1 }));
        }
    }
}
