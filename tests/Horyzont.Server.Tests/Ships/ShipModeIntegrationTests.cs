using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Console;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests.Ships;

[Collection(SqlitePools.Name)]
public sealed class ShipModeIntegrationTests
{
    private static readonly CellPos _cockpitCell = new(2, 2, 0);
    private static readonly Vector3 _origin = new(0, 4000, 0);

    private static ServerRuntimeOptions WithShipContent(ServerRuntimeOptions options) =>
        options with { BlockTypeKeys = ShipFixtures.BlockKeys, BlockDefinitions = ShipFixtures.Definitions };

    private static async Task<(ReplicationRig Rig, RigClient Client, EntityId Ship)> StartWithShipAsync()
    {
        var rig = await ReplicationRig.StartAsync(() => ShipFixtures.CreateWorld(null), configure: WithShipContent);
        var client = await rig.JoinAsync("alice", 1);
        var ship = rig.Runtime.State.EntityIds.PeekNext();
        foreach (var op in ShipFixtures.BuildOperations(ship, ShipFixtures.Thirty, _origin))
        {
            rig.Runtime.Simulation.ApplyServerOperation(op);
        }

        // Stand in front of the nose of the ship.
        var feet = _origin + new Vector3(2.5f, 0f, -2f);
        Assert.True(rig.Runtime.Simulation.UpdatePlayerKinematics(client.Player, FrameRef.Of(WorldFactory.HomePlanet), 0, feet, Quaternion.Identity, 0f));
        rig.Place(client.Player, feet);
        rig.Step(9);
        return (rig, client, ship);
    }

    private static ShipModeIntent Aim(ReplicationRig rig, RigClient client, EntityId ship, ConstructMode target)
    {
        rig.Runtime.State.Players.TryGet(client.Player, out var player);
        var eye = player!.EyePosition(rig.Runtime.Simulation.Rules);
        var center = _origin + new Vector3(_cockpitCell.X + 0.5f, _cockpitCell.Y + 0.5f, _cockpitCell.Z + 0.5f);
        return new ShipModeIntent(ship, _cockpitCell, target, eye, Vector3.Normalize(center - eye));
    }

    private static async Task<IntentReply> SubmitAsync(ReplicationRig rig, RigClient client, Intent intent)
    {
        var replies = client.Replies.Count;
        client.Session.SubmitIntent(intent);
        Assert.True(await rig.Rig.RunUntilAsync(() => client.Replies.Count > replies), "no intent reply");
        rig.Step(3);
        return client.Replies[^1];
    }

    [Fact]
    public async Task ShipMode_ThirtyBlockConstruct_ToShipAndBack_ViaIntents()
    {
        var (rig, client, ship) = await StartWithShipAsync();
        await using var owner = rig;
        var processor = new ConsoleCommandProcessor(rig.Runtime, NullLogger<ConsoleCommandProcessor>.Instance);
        var construct = rig.Runtime.State.Constructs.All.Single();
        Assert.Equal(30, construct.BlockCount);
        var staticHash = StateHasher.Hash(rig.Runtime.State);
        var version = construct.Version;
        var states = client.Received.OfType<ConstructState>().Count();

        var toShip = await SubmitAsync(rig, client, Aim(rig, client, ship, ConstructMode.Ship));

        Assert.Equal(RejectReason.None, toShip.Reason);
        Assert.Equal(ConstructMode.Ship, construct.Mode);
        Assert.Equal(version + 1, construct.Version);
        Assert.NotEqual(staticHash, StateHasher.Hash(rig.Runtime.State));
        Assert.True(rig.Runtime.ShipStats.TryGet(ship, out var stats));
        Assert.Equal(8210f, stats.MassKg);

        // The console line prints numbers that follow from the block data: 8210 kg, thrust of 2 x 60 000 N upward and 60 000 N
        // each way along Z, 2 stabilizers (80 000 N·m), 2 tanks (2000), 1 cockpit, 4 engines and 4 seats.
        var line = processor.Execute("ship " + ship.Value);
        Assert.StartsWith($"SHIP id={ship.Value} mode=Ship mass=8210kg com=(", line, StringComparison.Ordinal);
        Assert.EndsWith(") thrust=+X:0,-X:0,+Y:120000,-Y:0,+Z:60000,-Z:60000 torque=80000 fuel=2000 cockpits=1 engines=4 seats=4", line, StringComparison.Ordinal);

        var toStatic = await SubmitAsync(rig, client, Aim(rig, client, ship, ConstructMode.Static));

        Assert.Equal(RejectReason.None, toStatic.Reason);
        Assert.Equal(ConstructMode.Static, construct.Mode);
        Assert.Equal(version + 2, construct.Version);
        Assert.False(rig.Runtime.ShipStats.TryGet(ship, out _));
        Assert.StartsWith($"SHIP id={ship.Value} mode=Static mass=8210kg", processor.Execute("ship " + ship.Value), StringComparison.Ordinal);
        Assert.Equal("unknown construct 99", processor.Execute("ship 99"));
        Assert.Equal("usage: ship <construct id>", processor.Execute("ship"));

        // The client got the mode changes as operations, in order, and never a second transfer of the construct.
        var applied = client.Received.OfType<OperationApplied>().Where(m => m.Operation is ShipModeSetOp).ToList();
        Assert.Equal([ConstructMode.Ship, ConstructMode.Static], applied.Select(m => ((ShipModeSetOp)m.Operation).Mode));
        Assert.Equal([version + 1, version + 2], applied.Select(m => m.ConstructChanges[0].Version));
        Assert.Equal(states, client.Received.OfType<ConstructState>().Count());
    }

    [Fact]
    public async Task ShipModeSetOp_LiveObserver_ReceivesOperationNotTransfer()
    {
        var (rig, alice, ship) = await StartWithShipAsync();
        await using var owner = rig;
        var bob = await rig.JoinAsync("bob", 2, _origin + new Vector3(10, 0, 10));
        rig.Step(12);
        Assert.True(rig.Replication.Constructs.IsLive(bob.Player, ship));
        var transfersBefore = bob.Received.OfType<ConstructState>().Count();
        Assert.True(transfersBefore > 0);

        var reply = await SubmitAsync(rig, alice, Aim(rig, alice, ship, ConstructMode.Ship));
        Assert.Equal(RejectReason.None, reply.Reason);
        rig.Step(6);

        var op = Assert.Single(bob.Received.OfType<OperationApplied>(), m => m.Operation is ShipModeSetOp);
        Assert.Equal(ConstructMode.Ship, ((ShipModeSetOp)op.Operation).Mode);
        Assert.Equal(transfersBefore, bob.Received.OfType<ConstructState>().Count());
        Assert.True(rig.Replication.Constructs.IsLive(bob.Player, ship));
    }

    [Fact]
    public async Task ShipModeIntent_Rejected_NothingJournaledAndClientHearsTheReason()
    {
        var (rig, client, ship) = await StartWithShipAsync();
        await using var owner = rig;
        var hash = StateHasher.Hash(rig.Runtime.State);
        var last = rig.Runtime.State.LastOpId;
        var hull = Aim(rig, client, ship, ConstructMode.Ship) with { CockpitCell = new CellPos(2, 1, 1) };

        var reply = await SubmitAsync(rig, client, hull);

        Assert.Equal(RejectReason.NoCockpit, reply.Reason);
        Assert.Equal(hash, StateHasher.Hash(rig.Runtime.State));
        Assert.Equal(last, rig.Runtime.State.LastOpId);
    }
}
