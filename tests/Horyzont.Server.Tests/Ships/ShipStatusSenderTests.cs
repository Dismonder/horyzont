using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;
using Horyzont.Net.Session;
using Horyzont.Server.Ships;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Server.Tests.Ships;

/// <summary>K5-26 decision 9: <c>ShipStatus</c> 2 Hz to the players in the frame of a ship-mode construct, without allocating.</summary>
public sealed class ShipStatusSenderTests
{
    private static int StatusesOf(ShipCoopClient client, EntityId ship) =>
        client.Client.Received.OfType<ShipStatus>().Count(s => s.Ship == ship);

    [Fact]
    public async Task ShipStatusSender_2Hz_OnlyToPlayersInShipFrame()
    {
        await using var rig = await ShipCoopRig.StartAsync(7, lossy: false);
        var alice = await rig.JoinAsync("alice", 1);
        var bob = await rig.JoinAsync("bob", 2);
        var ship = rig.AddShip(rig.UpToward(1));
        rig.Seat(alice.Player, ship, new CellPos(1, 2, 1));
        rig.Stand(bob.Player, rig.Ship(ship).Origin + new Vector3(20f, 0, 0));
        rig.Steps(30);
        Assert.Equal(FrameRef.Of(ship), rig.PlayerOf(alice.Player).Frame);
        Assert.True(rig.Flight.TryGetStatus(ship, out _), rig.Describe(ship));

        foreach (var client in rig.Clients)
        {
            client.Client.Received.Clear();
        }

        rig.Steps(120);
        Assert.True(StatusesOf(alice, ship) == 4, $"{StatusesOf(alice, ship)} at {rig.ServerTick}, frame {rig.PlayerOf(alice.Player).Frame}, {rig.Describe(ship)}, all {alice.Client.Received.Count}");
        Assert.Empty(bob.Client.Received.OfType<ShipStatus>());
        var last = alice.Client.Received.OfType<ShipStatus>().Last();
        Assert.True(rig.Flight.TryGetStatus(ship, out var status));
        Assert.Equal(status.FuelCapacity, last.FuelCapacity);
        Assert.Equal(0ul, last.Tick.Value % ShipStatusSender.IntervalTicks);

        // A static construct is not a ship: no status.
        rig.SetMode(ship, ConstructMode.Static);
        rig.Steps(2);
        alice.Client.Received.Clear();
        rig.Steps(60);
        Assert.Equal(0, StatusesOf(alice, ship));
    }

    [Fact]
    public void ShipStatusSender_AllocatesZeroBytes()
    {
        using var transport = new CopyingTransport();
        var world = WorldFactory.CreateWithStarSystem(0);
        var simulation = new ServerSimulation(world, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        var session = new ServerSession(transport, new ServerOptions { BuildHash = 1 },
            new GameServerHooks(simulation, () => true, NullLogger<GameServerHooks>.Instance), new FakeTimeProvider());
        session.Poll(new Tick(1));
        var players = session.PlayersInGame;
        Assert.Equal(8, players.Count);
        var home = FrameRef.Of(world.HomePlanet);
        for (var i = 0; i < players.Count; i++)
        {
            var ship = world.EntityIds.PeekNext();
            simulation.ApplyServerOperation(new ConstructCreateOp(
                ship, home, new Vector3(10f * i, 4010, 0), Quaternion.Identity, new CellPos(0, 0, 0), BlockState.Create(1, 0)));
            Assert.True(world.Constructs.TryGet(ship, out var construct));
            simulation.ApplyServerOperation(TestShips.ModeOp(construct!, ConstructMode.Ship));
            Assert.True(world.Players.TryGet(players[i], out var player));
            simulation.ApplyServerOperation(new EntityReparentOp(
                player!.Entity, FrameRef.Of(ship), player.ParentSeq + 1, new Vector3(0.5f, 1, 0.5f), Quaternion.Identity, Vector3.Zero, null));
        }

        var fixedStatus = new FixedStatus();
        var sender = new ShipStatusSender(() => fixedStatus);
        Assert.Equal(8, sender.Send(session, world));
        for (var i = 0; i < 10; i++)
        {
            sender.Send(session, world);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var sent = 0;
        for (var i = 0; i < 1_000; i++)
        {
            sent += sender.Send(session, world);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(8_000, sent);
    }

    private sealed class FixedStatus : IShipStatusSource
    {
        public bool TryGetStatus(EntityId ship, out ShipFlightStatus status)
        {
            status = new ShipFlightStatus(10f, 100f, 250f, ShipFlightFlags.Parked, ShipLaunchBlock.None);
            return true;
        }
    }
}
