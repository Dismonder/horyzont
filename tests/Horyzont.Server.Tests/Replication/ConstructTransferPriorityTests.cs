using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Replication;
using Horyzont.Server.Tests.Support;

namespace Horyzont.Server.Tests.Replication;

/// <summary>K5-26 decision 5: transfers start with the recipient's own ship, then other ships by distance, then static constructs.</summary>
public sealed class ConstructTransferPriorityTests
{
    /// <summary>Bulk traffic passes only while the test hands out parts (one per grant).</summary>
    private sealed class PartsBudget : IReplicationSendBudget
    {
        public int Parts { get; set; }

        public bool TryConsume(PlayerId player, ReplicationTraffic traffic, int bytes)
        {
            if (traffic == ReplicationTraffic.Live)
            {
                return true;
            }

            if (Parts == 0)
            {
                return false;
            }

            Parts--;
            return true;
        }
    }

    private static int FirstPart(RigClient client, EntityId construct) =>
        client.Received.FindIndex(m => m is ConstructState s && s.Header.Id == construct);

    private static int LastPart(RigClient client, EntityId construct) =>
        client.Received.FindLastIndex(m => m is ConstructState s && s.Header.Id == construct);

    [Fact]
    public async Task ConstructTransfers_OwnShipFirst_ThenShipsByDistance_ThenStatic()
    {
        var budget = new PartsBudget();
        await using var rig = await ReplicationRig.StartAsync(sendBudget: budget);
        var alice = await rig.JoinAsync("alice", 1, new Vector3(0, 4000, 5_000));

        // Created in the reverse of the expected order, so neither id nor creation order explains the result.
        var still = rig.AddConstruct(new Vector3(5, 4000, 0));
        var far = rig.AddShip(new Vector3(300, 4010, 0));
        var near = rig.AddShip(new Vector3(100, 4010, 0));
        var own = rig.AddShip(new Vector3(0, 4010, 0));
        rig.Step(9);
        Assert.False(rig.Replication.Knows(alice.Player, own));

        // Alice boards her ship: all four enter in one round.
        rig.PlaceIn(alice.Player, FrameRef.Of(own), new Vector3(0.5f, 1, 0.5f));
        rig.Step(3);
        Assert.All(new[] { own, near, far, still }, c => Assert.True(rig.Replication.Constructs.IsTransferring(alice.Player, c)));

        for (var i = 0; i < 60; i++)
        {
            budget.Parts = 1;
            rig.Step();
        }

        Assert.All(new[] { own, near, far, still }, c => Assert.True(rig.Replication.Constructs.IsLive(alice.Player, c)));
        Assert.True(LastPart(alice, own) < FirstPart(alice, near), "own ship before other ships");
        Assert.True(LastPart(alice, near) < FirstPart(alice, far), "nearer ship before the farther one");
        Assert.True(LastPart(alice, far) < FirstPart(alice, still), "ships before static constructs");
    }

    [Fact]
    public async Task ConstructTransfers_StartedTransferNotPreempted()
    {
        var budget = new PartsBudget();
        await using var rig = await ReplicationRig.StartAsync(sendBudget: budget);
        var alice = await rig.JoinAsync("alice", 1, new Vector3(0, 4000, 0));

        // A static construct of 30 chunks, three parts (at most 12 chunks per part), is being sent.
        var cells = Enumerable.Range(1, 29).Select(i => new CellPos(i % 5 * 16, i / 5 % 3 * 16, i / 15 * 16)).ToArray();
        var big = rig.AddConstruct(new Vector3(20, 4000, 0), cells);
        rig.Step(3);
        budget.Parts = 1;
        rig.Step();
        Assert.True(FirstPart(alice, big) >= 0);
        Assert.True(rig.Replication.Constructs.IsTransferring(alice.Player, big));

        // Alice's own ship arrives: it waits until the started transfer completes.
        var own = rig.AddShip(new Vector3(0, 4010, 0));
        rig.PlaceIn(alice.Player, FrameRef.Of(own), new Vector3(0.5f, 1, 0.5f));
        for (var i = 0; i < 60; i++)
        {
            budget.Parts = 1;
            rig.Step();
        }

        Assert.True(rig.Replication.Constructs.IsLive(alice.Player, big));
        Assert.True(rig.Replication.Constructs.IsLive(alice.Player, own));
        Assert.True(LastPart(alice, big) < FirstPart(alice, own), "the started transfer was preempted");
    }
}
