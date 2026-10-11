using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Server.Characters;
using Horyzont.Server.Replication;

namespace Horyzont.Game.Server.Replication;

/// <summary>
/// The authoritative entities for replication: online players with the kinematics of their characters, and constructs with a bounding
/// sphere around their origin. Reads on the main thread only; copies into the caller's buffer without allocating.
/// </summary>
public sealed class ServerReplicationSource(WorldState world, IServerCharacterSource characters) : IEntityReplicationSource
{
    private const EntityStateFields AllFields =
        EntityStateFields.Position | EntityStateFields.Rotation | EntityStateFields.Velocity | EntityStateFields.MotionFlags;

    /// <inheritdoc />
    public int CopyEntities(Span<ReplicationEntity> destination)
    {
        var count = 0;
        var players = world.Players.All;
        for (var p = 0; p < players.Count; p++)
        {
            var player = players[p];
            if (!player.IsOnline || !characters.TryGetState(player.Player, out var state))
            {
                continue;
            }

            if (count == destination.Length)
            {
                return count;
            }

            destination[count++] = new ReplicationEntity(
                player.Entity, EntityKind.Player, player.Player, new SnapshotFrame(state.Frame, state.ParentSeq),
                new EntityState(player.Entity, AllFields, 0, state.ParentSeq, state.Feet, Quaternion.Normalize(state.BodyRotation), state.Velocity,
                    (byte)state.Mode),
                0f,
                state.Frame == player.Frame && state.ParentSeq == player.ParentSeq ? player.Seat : null);
        }

        var constructs = world.Constructs.All;
        for (var i = 0; i < constructs.Count; i++)
        {
            if (count == destination.Length)
            {
                return count;
            }

            var construct = constructs[i];

            // K5-26: a ship carries the velocity of its origin in the parent frame, so clients interpolate its flight with it.
            var velocity = construct.Mode == ConstructMode.Ship && world.ShipStates.TryGet(construct.Id, out var ship) ? ship.LinearVelocity : Vector3.Zero;
            destination[count++] = new ReplicationEntity(
                construct.Id, EntityKind.Construct, null, new SnapshotFrame(construct.Parent, construct.ParentSeq),
                new EntityState(construct.Id, AllFields, 0, construct.ParentSeq, construct.Origin, construct.Rotation, velocity, 0),
                BoundsRadius(construct));
        }

        destination[..count].Sort(static (a, b) => a.Id.Value.CompareTo(b.Id.Value));
        return count;
    }

    /// <inheritdoc />
    public uint GetLastProcessedInputSeq(PlayerId player) =>
        characters.TryGetState(player, out var state) ? state.LastProcessedInputSeq : 0;

    /// <summary>Radius of the sphere around the construct origin that contains every occupied cell (one cell is one metre).</summary>
    /// <param name="construct">The construct.</param>
    /// <returns>The radius in metres.</returns>
    public static float BoundsRadius(Construct construct)
    {
        ArgumentNullException.ThrowIfNull(construct);
        if (construct.BlockCount == 0)
        {
            return 0f;
        }

        var min = construct.BoundsMin;
        var max = construct.BoundsMax;
        var best = 0f;
        for (var corner = 0; corner < 8; corner++)
        {
            var x = (corner & 1) == 0 ? min.X : max.X + 1;
            var y = (corner & 2) == 0 ? min.Y : max.Y + 1;
            var z = (corner & 4) == 0 ? min.Z : max.Z + 1;
            best = MathF.Max(best, new Vector3(x, y, z).Length());
        }

        return best;
    }
}
