using System.Globalization;
using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Server.Tests.Ships;

/// <summary>
/// K5-26 acceptance (spec 02:234, 02:251): two clients have identical ships and passenger poses after a series of flights, and
/// passengers see a smooth flight with RTT 100 ms and 5 % losses. See the plan, section "Pomiar".
/// </summary>
public sealed class ShipReplicationAcceptanceTests(ITestOutputHelper output)
{
    private const double MaxFlightError = 0.5;
    private const double MaxJumpExcess = 0.5;
    private const int FlightFrames = 60 * 60 * 4;
    private static readonly CellPos _seat1 = new(1, 2, 1);
    private static readonly CellPos _seat2 = new(3, 2, 1);
    private static readonly CellPos _cockpit = new(2, 2, 0);

    [Theory]
    [Trait("Category", "Slow")]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ShipReplication_TwoClients_SeriesOfFlights_IdenticalShipsAndPassengers(int seed)
    {
        await using var rig = await ShipCoopRig.StartAsync(seed);
        var moon = 1;
        var planetB = 2;
        var up = rig.UpToward(moon);
        var side = Vector3D.Normalize(Vector3D.Cross(up, Math.Abs(up.Y) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0)));
        var radius = rig.Model.GetBody(0).RadiusMeters;
        var a = rig.AddShip(up);
        var b = rig.AddShip(Vector3D.Normalize(up + (side * (40 / radius))));
        var k1 = await rig.JoinAsync("g1", 1);
        var k3 = await rig.JoinAsync("g2", 2);
        var k2 = await rig.JoinAsync("g3", 3);
        var g3Feet = Vector3D.Normalize(up - (side * (200 / radius))) * radius;
        rig.Seat(k1.Player, a, _seat1);
        rig.Seat(k3.Player, b, _cockpit);
        rig.Stand(k2.Player, new Vector3((float)g3Feet.X, (float)g3Feet.Y, (float)g3Feet.Z));
        var flight = new FlightMeasure(rig, [(k1, a), (k3, b)]);
        foreach (var client in rig.Clients)
        {
            client.SnapshotTicks.Own = rig.EntityOf(client.Player);
        }

        // Stage 0: on the ground, everyone sees both ships and both passengers.
        rig.Settle(a, b);
        AssertIdentical(rig, "start");
        Assert.True(KnowsAll(rig, k2, a, b), "the observer 200 m away sees both ships");

        // In every flight: ships in the system frame within 10 km see each other (and their passengers), beyond 10.5 km they do not.
        var seenInSystem = 0;
        var partedInSystem = 0;
        void Watch()
        {
            flight.Measure();
            var shipA = rig.Ship(a);
            var shipB = rig.Ship(b);
            if (shipA.Parent.Kind != FrameKind.System || shipB.Parent.Kind != FrameKind.System)
            {
                return;
            }

            var distance = Vector3.Distance(shipA.Origin, shipB.Origin);
            if (distance <= 9_000 && rig.Replication.Knows(k1.Player, b) && rig.Replication.Knows(k3.Player, a)
                && rig.Replication.Knows(k1.Player, rig.EntityOf(k3.Player)))
            {
                seenInSystem++;
            }

            if (distance > 11_000)
            {
                Assert.False(rig.Replication.Knows(k1.Player, b), "ship B known beyond 11 km");
                Assert.False(rig.Replication.Knows(k3.Player, a), "ship A known beyond 11 km");
                Assert.False(rig.Replication.Knows(k1.Player, rig.EntityOf(k3.Player)), "passenger of B known beyond 11 km");
                partedInSystem++;
            }
        }

        void FlyBoth(int targetA, int targetB, string stage)
        {
            rig.Fly(a, targetA);
            if (!rig.IsFlying(b))
            {
                rig.Fly(b, targetB);
            }

            Assert.True(rig.StepUntil(() => !rig.IsFlying(a) && !rig.IsFlying(b), FlightFrames, Watch), $"{stage}: {rig.Describe(a)}; {rig.Describe(b)}");
            rig.Settle(a, b);
            AssertIdentical(rig, stage);
        }

        // Stage 1: both ships to the moon (planet → system → moon), side by side.
        FlyBoth(moon, moon, "moon");
        Assert.False(KnowsAll(rig, k2, a), "the observer at home does not see ships on the moon");
        Assert.True(seenInSystem > 0, "ships flying side by side in the system frame see each other");

        // Stage 2: G1 changes seats during the stop.
        rig.Seat(k1.Player, a, _seat2);
        rig.Settle(a, b);
        AssertIdentical(rig, "seat change");

        // Stage 3: B to planet B, A after it 15 s later on the same route: both in the system frame more than 10.5 km apart (the plan
        // sends A home from the moon, but its path to the home planet barely enters the system frame while B is there).
        rig.Fly(b, planetB);
        rig.Steps(15 * 60, Watch);
        FlyBoth(planetB, planetB, "apart");
        Assert.True(partedInSystem > 0, "ships never parted beyond 11 km in the system frame");

        // Stage 4: both home.
        FlyBoth(0, 0, "home");

        // G3 walks to where A landed (the pilot lands toward the target centre, not on the launch pad).
        var landed = rig.Ship(a).Origin;
        rig.Stand(k2.Player, Vector3.Normalize(landed) * (float)radius);
        rig.Steps(120);
        rig.SetMode(a, ConstructMode.Static);
        rig.SetMode(b, ConstructMode.Static);
        rig.Steps(120);
        AssertIdentical(rig, "static");
        Assert.True(KnowsAll(rig, k2, a), "the observer sees ship A back home");

        output.WriteLine(flight.Report());
        foreach (var client in rig.Clients)
        {
            var statuses = client.Client.Received.Count(m => m is Core.Protocol.Messages.ShipStatus);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{client.Client.Name}: resyncs={client.Resyncs} wrongFrame={client.Entities.WrongFrameApplied} shipStatus={statuses} "
                + $"snapshotBytes={Bytes<Core.Protocol.Messages.Snapshot>(client)} bulkBytes={Bytes<Core.Protocol.Messages.ConstructState>(client)} "
                + $"inShipSnapshots={client.SnapshotTicks.InShip} withoutOwnShip={client.SnapshotTicks.OwnShipMissing}"));
            Assert.Equal(0, client.Entities.WrongFrameApplied);
            Assert.Equal(0, client.SnapshotTicks.OwnShipMissing);
        }

        // ShipStatus (31) reaches the passengers of both ships, never the observer on the planet.
        Assert.Contains(k1.Client.Received, m => m is Core.Protocol.Messages.ShipStatus s && s.Ship == a);
        Assert.Contains(k3.Client.Received, m => m is Core.Protocol.Messages.ShipStatus s && s.Ship == b);
        Assert.DoesNotContain(k2.Client.Received, m => m is Core.Protocol.Messages.ShipStatus);

        Assert.True(flight.MaxError <= MaxFlightError, flight.Report());
        Assert.True(flight.MaxJump <= MaxJumpExcess, flight.Report());
        Assert.True(flight.Samples > 1000, flight.Report());
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ShipReplication_ObserverLeavesAndReturns_StateRebuilt()
    {
        await using var rig = await ShipCoopRig.StartAsync(3);
        var up = rig.UpToward(1);
        var side = Vector3D.Normalize(Vector3D.Cross(up, Math.Abs(up.Y) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0)));
        var radius = rig.Model.GetBody(0).RadiusMeters;
        var ship = rig.AddShip(up);
        var passenger = await rig.JoinAsync("g1", 1);
        var observer = await rig.JoinAsync("g3", 3);
        var feet = Vector3D.Normalize(up - (side * (200 / radius))) * radius;
        rig.Seat(passenger.Player, ship, _seat1);
        rig.Stand(observer.Player, new Vector3((float)feet.X, (float)feet.Y, (float)feet.Z));
        rig.Settle(ship);
        AssertIdentical(rig, "start");
        var states = observer.Client.Received.Count(m => m is Core.Protocol.Messages.ConstructState s && s.Header.Id == ship);

        // Away beyond 550 m of the observer and back (kinematics in ship mode, the only way a ship moves).
        var construct = rig.Ship(ship);
        var origin = construct.Origin;
        var away = origin + (Vector3.Normalize(origin) * 1_000f);
        rig.Pilots.Stop(ship);
        Assert.True(rig.Runtime.Simulation.UpdateConstructKinematics(ship, construct.Parent, construct.ParentSeq, away, construct.Rotation));
        rig.Steps(30);
        Assert.False(rig.Replication.Knows(observer.Player, ship));
        Assert.False(rig.Replication.Knows(observer.Player, rig.EntityOf(passenger.Player)));
        Assert.False(observer.Entities.TryGetKind(ship, out _));
        Assert.False(observer.Entities.TryGetKind(rig.EntityOf(passenger.Player), out _));

        Assert.True(rig.Runtime.Simulation.UpdateConstructKinematics(ship, construct.Parent, construct.ParentSeq, origin, construct.Rotation));
        rig.Steps(120);
        Assert.True(observer.Client.Received.Count(m => m is Core.Protocol.Messages.ConstructState s && s.Header.Id == ship) > states);
        AssertIdentical(rig, "returned");
    }

    private static long Bytes<T>(ShipCoopClient client)
        where T : Core.Protocol.Messages.Message
    {
        var writer = new Core.Serialization.ByteWriter();
        foreach (var message in client.Client.Received.OfType<T>())
        {
            Core.Serialization.MessageCodec.Write(writer, message);
        }

        return writer.Length;
    }

    private static bool KnowsAll(ShipCoopRig rig, ShipCoopClient client, params EntityId[] entities) =>
        entities.All(e => rig.Replication.Knows(client.Player, e) && client.Entities.TryGetKind(e, out _));

    /// <summary>Decision 12 of the plan: bitwise for fields of operations and complete states, snapshot positions bitwise, rotations within two quanta.</summary>
    private static void AssertIdentical(ShipCoopRig rig, string stage)
    {
        var state = rig.Runtime.State;
        foreach (var client in rig.Clients)
        {
            var who = stage + "/" + client.Client.Name;
            foreach (var id in client.Entities.Ids)
            {
                Assert.True(rig.Replication.Knows(client.Player, id), $"{who}: the client holds {id.Value} the server did not send");
            }

            foreach (var construct in state.Constructs.All)
            {
                if (!rig.Replication.Knows(client.Player, construct.Id))
                {
                    Assert.False(client.Entities.TryGetKind(construct.Id, out _), $"{who}: ship {construct.Id.Value} out of range but held");
                    continue;
                }

                Assert.True(client.Constructs.TryGetConstruct(construct.Id, out var copy), $"{who}: ship {construct.Id.Value} has no complete state");
                Assert.Equal(construct.Mode, copy.Mode);
                Assert.Equal(construct.Parent, copy.Parent);
                Assert.Equal(construct.ParentSeq, copy.ParentSeq);
                Assert.Equal(construct.Version, copy.Version);
                Assert.Equal(Blocks(construct), Blocks(copy));
                if (construct.Mode == ConstructMode.Static)
                {
                    Assert.True(Same(construct.Origin, copy.Origin), $"{who}: static origin {construct.Origin} vs {copy.Origin}");
                    Assert.True(Same(construct.Rotation, copy.Rotation), $"{who}: static rotation");
                    continue;
                }

                Assert.True(client.Entities.TryGetPose(construct.Id, out var frame, out var pose), $"{who}: ship {construct.Id.Value} has no snapshot");
                Assert.Equal(construct.Parent, frame.Frame);
                Assert.Equal(construct.ParentSeq, pose.ParentSeq);
                Assert.True(Same(construct.Origin, pose.Position), $"{who}: ship position {construct.Origin} vs {pose.Position}");
                Assert.True(Angle(construct.Rotation, pose.Rotation) <= 2 * Core.Serialization.Quantization.RotationQuantumRadians, $"{who}: ship rotation");
            }

            foreach (var player in state.Players.All)
            {
                if (!player.IsOnline || !rig.Replication.Knows(client.Player, player.Entity))
                {
                    continue;
                }

                Assert.True(client.Entities.TryGetPose(player.Entity, out var frame, out var pose), $"{who}: player {player.Player.Value} unknown");
                Assert.Equal(player.Frame, frame.Frame);
                Assert.Equal(player.ParentSeq, pose.ParentSeq);
                Assert.True(Same(player.Position, pose.Position), $"{who}: player position {player.Position} vs {pose.Position}");
                if (player.Seat is { } seat)
                {
                    Assert.True(client.Entities.TryGetSeat(player.Entity, out var copySeat), $"{who}: player {player.Player.Value} seat unknown");
                    Assert.Equal(seat, copySeat);
                }
                else
                {
                    Assert.False(client.Entities.TryGetSeat(player.Entity, out _), $"{who}: player {player.Player.Value} seated on the client only");
                }
            }
        }
    }

    private static ulong[] Blocks(Construct construct) =>
        [.. construct.Chunks.OrderBy(c => c.Key).SelectMany(c => c.Value.Blocks.ToArray().Select(b => (ulong)b))];

    private static bool Same(Vector3 a, Vector3 b) =>
        BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X)
        && BitConverter.SingleToInt32Bits(a.Y) == BitConverter.SingleToInt32Bits(b.Y)
        && BitConverter.SingleToInt32Bits(a.Z) == BitConverter.SingleToInt32Bits(b.Z);

    private static bool Same(Quaternion a, Quaternion b) => Same(new Vector3(a.X, a.Y, a.Z), new Vector3(b.X, b.Y, b.Z))
        && BitConverter.SingleToInt32Bits(a.W) == BitConverter.SingleToInt32Bits(b.W);

    private static double Angle(Quaternion a, Quaternion b)
    {
        var dot = Math.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b)));
        return 2 * Math.Acos(Math.Min(1.0, dot));
    }

    /// <summary>In flight, every frame: world pose of each passenger's ship and of the passenger at the render tick, client vs server.</summary>
    private sealed class FlightMeasure(ShipCoopRig rig, (ShipCoopClient Client, EntityId Ship)[] passengers)
    {
        private readonly ShipPoseMeasure _ships = new(rig);
        private readonly ShipPoseMeasure _passengers = new(rig);

        public double MaxError => Math.Max(_ships.MaxError, _passengers.MaxError);

        public double MaxJump => Math.Max(_ships.MaxJump, _passengers.MaxJump);

        public int Samples => _ships.Samples;

        public void Measure()
        {
            foreach (var (client, ship) in passengers)
            {
                _ships.Sample(client, ship);

                // The passenger in the world: the ship pose composed with the local pose of the passenger on each side.
                var player = rig.PlayerOf(client.Player);
                if (player.Frame == FrameRef.Of(ship) && client.Entities.TryGetPose(player.Entity, out var frame, out var local)
                    && frame.Frame == player.Frame)
                {
                    _passengers.Sample(
                        client, ship, FrameMath.FromFloat(player.Position, player.BodyRotation), FrameMath.FromFloat(local.Position, local.Rotation));
                }
                else
                {
                    _passengers.Reset(client, ship);
                }
            }
        }

        public string Report() => "ships: " + _ships.Report() + "; passengers: " + _passengers.Report();
    }
}
