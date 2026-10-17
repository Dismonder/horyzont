using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Survival;
using Horyzont.Core.World;

namespace Horyzont.Core.Simulation;

/// <summary>Where the tombstones of a death stand: a pose in the frame of a planet, prepared by the server (K6-35 D16, D17).</summary>
/// <param name="Planet">The planet.</param>
/// <param name="Position">Position of the tombstones.</param>
/// <param name="Rotation">Rotation of the tombstones.</param>
public readonly record struct TombstonePlacement(PlanetId Planet, Vector3 Position, Quaternion Rotation);

/// <summary>Builds the one operation of a death (K6-35 D1): a compound, or a single reparent when nothing else changes.</summary>
public static class DeathOperationBuilder
{
    /// <summary>
    /// Builds <c>[tombstone entity, its container, the transfers] per tombstone, the food cleared, and last the reparent of the player with the vitals reset</c> (D6). Ids
    /// come from the next values of the allocators. The operation carries every value from the content: the stack limit of each transfer is the larger of the limit of
    /// its item and the count moved (D9).
    /// </summary>
    /// <param name="state">World state.</param>
    /// <param name="player">The dying player.</param>
    /// <param name="penalty">The penalty of the world.</param>
    /// <param name="placement">Where the tombstones stand.</param>
    /// <param name="respawn">Where the player appears.</param>
    /// <param name="stackLimit">The stack limit of an item type from the content.</param>
    /// <returns>The operation to apply with <see cref="ServerSimulation.ApplyServerOperation"/>.</returns>
    /// <exception cref="ArgumentException">The player is unknown.</exception>
    public static Operation Build(
        WorldState state, PlayerId player, DeathPenalty penalty, TombstonePlacement placement, RespawnPoint respawn, Func<ItemTypeId, ushort> stackLimit)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(stackLimit);
        if (!state.Players.TryGet(player, out var body))
        {
            throw new ArgumentException("Unknown player.", nameof(player));
        }

        var plan = DeathInventoryPlan.Create(state, player, penalty);
        var components = new List<Operation>();
        var nextEntity = state.EntityIds.PeekNext().Value;
        var nextContainer = state.ContainerIds.PeekNext().Value;
        for (var t = 0; t < plan.Tombstones.Count; t++)
        {
            var tombstone = plan.Tombstones[t];
            var entity = new EntityId(nextEntity++);
            var container = new ContainerId(nextContainer++);
            components.Add(new PersistentEntityCreateOp(
                entity, PersistentEntityKind.Tombstone, FrameRef.Of(placement.Planet), 0, placement.Position, placement.Rotation, player));
            components.Add(new ContainerCreateOp(
                container, ContainerKind.Tombstone, ContainerHost.PersistentEntity(entity), player, ContainerAccess.Private, tombstone.Size));
            for (var i = 0; i < tombstone.Transfers.Count; i++)
            {
                // The stack limit of the content, or the count when an old stack is larger: nothing is split and nothing is refused (D9).
                var transfer = tombstone.Transfers[i];
                components.Add(new ItemTransferOp(
                    transfer.From, new SlotRef(container, transfer.TargetSlot), transfer.Type, transfer.Count, Math.Max(stackLimit(transfer.Type), transfer.Count)));
            }
        }

        // Every penalty takes the food (D3); a record that holds nothing else goes with it (the applier keeps the discoveries and the bed).
        if (state.Progress.TryGet(player, out var progress) && !progress.Food.IsEmpty)
        {
            components.Add(new FoodSetOp(player, default));
        }

        // The last component, the only one without undo (D6): the body appears at the respawn with the next counter and fresh vitals (D5).
        components.Add(new EntityReparentOp(
            body.Entity, FrameRef.Of(respawn.Pose.Planet), body.ParentSeq + 1, respawn.Pose.Position, respawn.Pose.Rotation, Vector3.Zero, null, ResetVitals: true));
        return components.Count == 1 ? components[0] : new CompoundOp(components.ToArray());
    }
}
