using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Tests.Support;

namespace Horyzont.Server.Tests.Replication;

/// <summary>K5-26 decision 7: the spawn of a seated player carries its seat, so an observer entering range later knows it.</summary>
public sealed class SeatSpawnTests
{
    [Fact]
    public async Task EntitySpawn_SeatedPlayer_KindDataCarriesSeat()
    {
        await using var rig = await ReplicationRig.StartAsync();
        var bob = await rig.JoinAsync("bob", 2);
        var ship = rig.AddShip(new Vector3(0, 4010, 0));
        var bobEntity = rig.EntityOf(bob.Player);
        var seat = new CellPos(0, 0, 0);
        rig.Runtime.Simulation.ApplyServerOperation(new EntityReparentOp(
            bobEntity, FrameRef.Of(ship), 1, Vector3.Zero, Quaternion.Identity, Vector3.Zero, seat));
        rig.PlaceIn(bob.Player, FrameRef.Of(ship), Vector3.Zero, 1);
        rig.Step(3);

        // Alice enters range after the boarding: Bob's spawn carries the seat, hers (unseated) carries nothing.
        var alice = await rig.JoinAsync("alice", 1);
        rig.Step(6);
        var spawn = alice.Received.OfType<EntitySpawn>().Last(s => s.Entity == bobEntity);
        Assert.Equal(FrameRef.Of(ship), spawn.Frame.Frame);
        Assert.Equal(EntitySpawnSeat.Encode(seat), spawn.KindData);
        var aliceEntity = rig.EntityOf(alice.Player);
        Assert.Empty(bob.Received.OfType<EntitySpawn>().Last(s => s.Entity == aliceEntity).KindData);
        Assert.All(alice.Received.OfType<EntitySpawn>().Where(s => s.Kind == EntityKind.Construct), s => Assert.Empty(s.KindData));
    }
}
