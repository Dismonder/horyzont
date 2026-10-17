using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.World;

namespace Horyzont.Core.Simulation;

/// <summary>
/// The recovery of a tombstone (K6-35 D13): a tombstone is a source only, and what empties it takes it away. The resolvers of moving and of taking all call
/// <see cref="CompleteRemoval"/> on the operation they built, so the last item, the removal of the container and the removal of the entity are one operation.
/// </summary>
internal static class TombstoneRecovery
{
    /// <summary>True for the container of a tombstone: hosted by a persistent entity, of the kind <see cref="ContainerKind.Tombstone"/>.</summary>
    internal static bool IsTombstone(Container container) =>
        container.Host.Kind == ContainerHostKind.PersistentEntity && container.Kind == ContainerKind.Tombstone;

    /// <summary>
    /// Appends <c>ContainerRemoveOp</c> and <c>PersistentEntityRemoveOp</c> for every tombstone that the transfers of the operation empty. The operation is returned unchanged when it
    /// empties none (or when the two more components would not fit a compound; that case is unreachable: a tombstone holds at most 256 slots, so a move is one transfer plus two components and a take-all fits too).
    /// </summary>
    /// <param name="state">World state, before the operation.</param>
    /// <param name="operation">A transfer or a compound of transfers built by a resolver.</param>
    /// <returns>The operation, completed.</returns>
    internal static Operation CompleteRemoval(WorldState state, Operation operation)
    {
        // A move or take-all that touches no tombstone is the common case: decide that without allocating (review A, P3-3).
        if (!TakesFromTombstone(state, operation))
        {
            return operation;
        }

        var parts = operation is CompoundOp compound ? compound.Components : [operation];
        List<ContainerId>? emptied = null;
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i] is not ItemTransferOp transfer || !state.Containers.TryGet(transfer.From.Container, out var source) || !IsTombstone(source)
                || (emptied?.Contains(source.Id) ?? false))
            {
                continue;
            }

            if (EmptiesContainer(source, parts))
            {
                (emptied ??= []).Add(source.Id);
            }
        }

        if (emptied is null || parts.Count + (2 * emptied.Count) > ProtocolConstants.MaxCompoundComponents)
        {
            return operation;
        }

        var all = new List<Operation>(parts.Count + (2 * emptied.Count));
        all.AddRange(parts);
        foreach (var id in emptied)
        {
            state.Containers.TryGet(id, out var container);
            all.Add(new ContainerRemoveOp(id));
            all.Add(new PersistentEntityRemoveOp(new EntityId(container!.Host.Id)));
        }

        return new CompoundOp(all.ToArray());
    }

    private static bool TakesFromTombstone(WorldState state, Operation operation)
    {
        if (operation is ItemTransferOp single)
        {
            return state.Containers.TryGet(single.From.Container, out var source) && IsTombstone(source);
        }

        if (operation is CompoundOp compound)
        {
            for (var i = 0; i < compound.Components.Count; i++)
            {
                if (compound.Components[i] is ItemTransferOp transfer && state.Containers.TryGet(transfer.From.Container, out var source) && IsTombstone(source))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>True when the transfers out of the container leave every slot empty.</summary>
    private static bool EmptiesContainer(Container container, IReadOnlyList<Operation> parts)
    {
        var left = new int[container.Size];
        for (var slot = 0; slot < left.Length; slot++)
        {
            left[slot] = container.GetSlot(slot).Count;
        }

        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i] is ItemTransferOp transfer && transfer.From.Container == container.Id && transfer.From.Slot < left.Length)
            {
                left[transfer.From.Slot] -= transfer.Count;
            }
        }

        foreach (var count in left)
        {
            if (count > 0)
            {
                return false;
            }
        }

        return true;
    }
}
