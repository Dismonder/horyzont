using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Markers;
using Horyzont.Server.Markers;
using Horyzont.Server.Replication;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing;

namespace Horyzont.Server.Tests.Markers;

[Collection(SqlitePools.Name)]
public sealed class MarkerReplicationTests
{
    private static MarkerSetIntent New(bool shared, string name) =>
        new(MarkerId.None, shared, WorldFactory.HomePlanet, new Vector3(1, 4000, 2), name, MarkerIcon.Poi, MarkerColor.Red);

    private static MarkerSetOp Op(ulong id, PlayerId? owner, string name) =>
        new(new MarkerId(id), owner, WorldFactory.HomePlanet, new Vector3(1, 4000, 2), name, MarkerIcon.Poi, MarkerColor.Red);

    private static async Task<(ServerRig Rig, MarkerReplication Replication)> StartAsync(IReplicationSendBudget? budget = null)
    {
        var rig = new ServerRig();
        var replication = MarkerReplication.Register(rig.Runtime, () => budget);
        await rig.StartAsync();
        return (rig, replication);
    }

    private static List<MarkerSetOp> MarkerOps(RigClient client) =>
        [.. client.Received.OfType<OperationApplied>().Select(m => m.Operation).OfType<MarkerSetOp>()];

    private static string Bytes(RigClient client)
    {
        var all = new ByteWriter();
        foreach (var message in client.Received.Where(m => m is MarkersState or OperationApplied))
        {
            MessageCodec.Write(all, message);
        }

        return Convert.ToHexString(all.WrittenSpan);
    }

    [Fact]
    public async Task MarkersState_OnReady_SharedAndOwnPrivateOnly()
    {
        var (rig, replication) = await StartAsync();
        await using var owner = rig;
        // Markers from before anyone joined: private ones of the first two players to come (ids 1 and 2) and a shared one.
        rig.Runtime.Simulation.ApplyServerOperation(Op(1, new PlayerId(1), "Alice only"));
        rig.Runtime.Simulation.ApplyServerOperation(Op(2, new PlayerId(2), "Bob only"));
        rig.Runtime.Simulation.ApplyServerOperation(Op(3, null, "Everyone"));

        var alice = await rig.JoinAsync("alice", 1);
        var bob = await rig.JoinAsync("bob", 2);
        var carol = await rig.JoinAsync("carol", 3);
        rig.StepMany(5);

        Assert.Equal(new PlayerId(1), alice.Player);
        Assert.Equal(["Alice only", "Everyone"], Assert.Single(alice.Received.OfType<MarkersState>()).Markers.Select(m => m.Name));
        Assert.Equal(["Bob only", "Everyone"], Assert.Single(bob.Received.OfType<MarkersState>()).Markers.Select(m => m.Name));
        Assert.Equal(["Everyone"], Assert.Single(carol.Received.OfType<MarkersState>()).Markers.Select(m => m.Name));
        Assert.DoesNotContain(Convert.ToHexString("Alice only"u8), Bytes(carol));
        Assert.DoesNotContain(Convert.ToHexString("Alice only"u8), Bytes(bob));
        Assert.Equal(3, replication.StatesSent);
    }

    [Fact]
    public async Task MarkerReplication_PrivateToOwnerOnly_SharedToAll()
    {
        var (rig, _) = await StartAsync();
        await using var owner = rig;
        var alice = await rig.JoinAsync("alice", 1);
        var bob = await rig.JoinAsync("bob", 2);
        rig.StepMany(3);

        alice.Session.SubmitIntent(New(shared: false, "Secret camp"));
        alice.Session.SubmitIntent(New(shared: true, "Open camp"));
        Assert.True(await rig.RunUntilAsync(() => alice.Replies.Count >= 2));
        rig.StepMany(5);

        Assert.All(alice.Replies, reply => Assert.Equal(RejectReason.None, reply.Reason));
        Assert.Equal(["Secret camp", "Open camp"], MarkerOps(alice).Select(o => o.Name));
        Assert.Equal(["Open camp"], MarkerOps(bob).Select(o => o.Name));
        // No byte of the private marker reaches the second client: not its name, not its operation.
        Assert.DoesNotContain(Convert.ToHexString("Secret camp"u8), Bytes(bob));
        Assert.Contains(Convert.ToHexString("Secret camp"u8), Bytes(alice));
    }

    [Fact]
    public async Task MarkerReplication_RemovePrivate_OnlyOwnerNotified()
    {
        var (rig, _) = await StartAsync();
        await using var owner = rig;
        var alice = await rig.JoinAsync("alice", 1);
        var bob = await rig.JoinAsync("bob", 2);
        rig.Runtime.Simulation.ApplyServerOperation(Op(1, alice.Player, "Secret"));
        rig.StepMany(3);

        alice.Session.SubmitIntent(new MarkerRemoveIntent(new MarkerId(1)));
        Assert.True(await rig.RunUntilAsync(() => alice.Replies.Count >= 1));
        rig.StepMany(5);

        Assert.Single(alice.Received.OfType<OperationApplied>(), m => m.Operation is MarkerRemoveOp);
        Assert.DoesNotContain(bob.Received.OfType<OperationApplied>(), m => m.Operation is MarkerRemoveOp);
        Assert.Empty(rig.Runtime.State.Markers.All);
    }

    [Fact]
    public async Task MarkerReplication_NotReady_NothingSent()
    {
        var (rig, replication) = await StartAsync();
        await using var owner = rig;
        var sent = replication.OperationsSent;

        rig.Runtime.Simulation.ApplyServerOperation(Op(1, null, "Early"));
        rig.StepMany(3);

        Assert.Equal(sent, replication.OperationsSent);
        Assert.Equal(0, replication.StatesSent);
    }

    [Fact]
    public async Task MarkerReplication_ClosedBudget_StateWaitsThenArrivesWithoutDuplicates()
    {
        var budget = new GatedSendBudget { Open = false };
        var (rig, replication) = await StartAsync(budget);
        await using var owner = rig;
        var alice = await rig.JoinAsync("alice", 1);
        rig.StepMany(3);
        Assert.Equal(0, replication.StatesSent);

        // An operation while the state waits is not sent separately: the state, built later, contains it.
        rig.Runtime.Simulation.ApplyServerOperation(Op(1, null, "During the wait"));
        rig.StepMany(3);
        Assert.Empty(MarkerOps(alice));

        budget.Open = true;
        rig.StepMany(5);

        var state = Assert.Single(alice.Received.OfType<MarkersState>());
        Assert.Equal(["During the wait"], state.Markers.Select(m => m.Name));
        Assert.Empty(MarkerOps(alice));
        Assert.True(budget.BulkBytes > 0);

        rig.Runtime.Simulation.ApplyServerOperation(Op(2, null, "After"));
        rig.StepMany(3);
        Assert.Equal(["After"], MarkerOps(alice).Select(o => o.Name));
        Assert.True(budget.LiveBytes > 0);
    }
}
