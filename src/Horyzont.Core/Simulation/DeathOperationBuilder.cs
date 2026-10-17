using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
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
    public static Operation Build(
        WorldState state, PlayerId player, DeathPenalty penalty, TombstonePlacement placement, RespawnPoint respawn, Func<ItemTypeId, ushort> stackLimit) =>
        throw new NotImplementedException();
}
