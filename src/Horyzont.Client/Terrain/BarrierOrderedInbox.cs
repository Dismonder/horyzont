using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Terrain;

/// <summary>
/// The blocks the server sent and the edit barriers that followed them, in arrival order. A barrier says that the blocks of an
/// accepted edit arrived, so it may be acted on only after every block queued before it was applied (applying takes a few blocks per
/// frame); acting on it earlier would take the edit's overlay away while its blocks still wait, and the dug hole would pop back.
/// Main thread only.
/// </summary>
/// <typeparam name="TBlock">A block waiting to be applied.</typeparam>
public sealed class BarrierOrderedInbox<TBlock>
{
    private readonly Queue<Item> _items = new();

    /// <summary>Gets the number of queued blocks and barriers.</summary>
    public int Count => _items.Count;

    /// <summary>Queues a block.</summary>
    /// <param name="block">The block.</param>
    public void EnqueueBlock(TBlock block) => _items.Enqueue(new Item(block, default, null));

    /// <summary>Queues a barrier behind the blocks that arrived so far.</summary>
    /// <param name="operation">The operation the barrier belongs to.</param>
    /// <param name="entries">The final blocks of the edit.</param>
    public void EnqueueBarrier(OpId operation, IReadOnlyList<TerrainSyncEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _items.Enqueue(new Item(default, operation, entries));
    }

    /// <summary>Drops everything that waits.</summary>
    public void Clear() => _items.Clear();

    /// <summary>Applies queued items in order: at most <paramref name="maxBlocks"/> blocks, every barrier that is reached on the way.</summary>
    /// <param name="maxBlocks">Block budget of this call.</param>
    /// <param name="apply">Applies a block; false stops the drain (the session is ending).</param>
    /// <param name="barrier">Called for a barrier once the blocks before it were applied.</param>
    public void Drain(int maxBlocks, Func<TBlock, bool> apply, Action<OpId, IReadOnlyList<TerrainSyncEntry>> barrier)
    {
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(barrier);
        var applied = 0;
        while (_items.Count > 0)
        {
            var next = _items.Peek();
            if (next.Entries is not null)
            {
                _items.Dequeue();
                barrier(next.Operation, next.Entries);
                continue;
            }

            if (applied >= maxBlocks)
            {
                return;
            }

            _items.Dequeue();
            applied++;
            if (!apply(next.Block!))
            {
                return;
            }
        }
    }

    private readonly record struct Item(TBlock? Block, OpId Operation, IReadOnlyList<TerrainSyncEntry>? Entries);
}
