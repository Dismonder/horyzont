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

        // Stage 0: on the ground, everyone sees both ships and both passengers.
        rig.Settle(a, b);
        AssertIdentical(rig, "start");
        Assert.True(KnowsAll(rig, k2, a, b), "the observer 200 m away sees both ships");

        // Stage 1: both ships to the moon (planet → system → moon).
        rig.Fly(a, moon);
        rig.Fly(b, moon);
        Assert.True(rig.StepUntil(() => !rig.IsFlying(a) && !rig.IsFlying(b), FlightFrames, flight.Measure), "flight to the moon");
        rig.Settle(a, b);
        AssertIdentical(rig, "moon");
        Assert.False(KnowsAll(rig, k2, a), "the observer at home does not see ships on the moon");

        // Stage 2: G1 changes seats during the stop.
        rig.Seat(k1.Player, a, _seat2);
        rig.Settle(a, b);
        AssertIdentical(rig, "seat change");

        // Stage 3: B to planet B, A home: they leave the moon together (in range in the system frame), then part beyond 10.5 km.
        rig.Fly(b, planetB);
        rig.Fly(a, 0);
        var seenInSystem = false;
        var partedInSystem = false;
        Assert.True(rig.StepUntil(() => !rig.IsFlying(a) && !rig.IsFlying(b), FlightFrames, () =>
        {
            flight.Measure();
            var shipA = rig.Ship(a);
            var shipB = rig.Ship(b);
            if (shipA.Parent.Kind == FrameKind.System && shipB.Parent.Kind == FrameKind.System)
            {
                var distance = Vector3.Distance(shipA.Origin, shipB.Origin);
                if (distance <= 9_000 && rig.Replication.Knows(k1.Player, b) && rig.Replication.Knows(k3.Player, a))
                {
                    seenInSystem = true;
                }

                if (distance > 11_000 && !rig.Replication.Knows(k1.Player, b) && !rig.Replication.Knows(k3.Player, a)
                    && !rig.Replication.Knows(k1.Player, rig.EntityOf(k3.Player)))
                {
                    partedInSystem = true;
                }
            }
        }), "flights from the moon");
        Assert.True(seenInSystem, "ships within 10 km in the system frame see each other");
        Assert.True(partedInSystem, "ships beyond 10.5 km in the system frame do not see each other (nor their passengers)");
        rig.Settle(a, b);
        AssertIdentical(rig, "apart");

        // Stage 4: B home too; both park as static constructs: the headers are bit-identical.
        rig.Fly(b, 0);
        Assert.True(rig.StepUntil(() => !rig.IsFlying(b), FlightFrames, flight.Measure), "flight of B home");
        rig.Settle(a, b);
        AssertIdentical(rig, "home");
        rig.SetMode(a, ConstructMode.Static);
        rig.SetMode(b, ConstructMode.Static);
        rig.Steps(120);
        AssertIdentical(rig, "static");
        Assert.True(KnowsAll(rig, k2, a, b), "the observer sees both ships back home");

        output.WriteLine(flight.Report());
        foreach (var client in rig.Clients)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{client.Client.Name}: resyncs={client.Resyncs} wrongFrame={client.Entities.WrongFrameApplied}"));
            Assert.Equal(0, client.Entities.WrongFrameApplied);
        }

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
        private readonly Dictionary<ShipCoopClient, (Vector3D Client, Vector3D Server)> _last = [];
        private readonly List<double> _errors = [];

        public double MaxError { get; private set; }

        public double MaxJump { get; private set; }

        public int Samples => _errors.Count;

        public int Missing { get; private set; }

        public void Measure()
        {
            var tick = rig.ServerTick - ShipCoopRig.RenderDelayTicks;
            foreach (var (client, ship) in passengers)
            {
                var frame = FrameRef.Of(ship);
                if (!rig.ServerHistory.TryGetWorldPose(frame, tick, out var server))
                {
                    continue;
                }

                if (!client.Frames.TryGetWorldPose(frame, tick, out var seen))
                {
                    Missing++;
                    _last.Remove(client);
                    continue;
                }

                // The passenger in the world: the ship pose composed with the local pose of the passenger (the same on both sides
                // when the snapshot positions agree; checked at every stop).
                var player = rig.PlayerOf(client.Player);
                if (player.Frame != frame || !client.Entities.TryGetPose(player.Entity, out _, out var local))
                {
                    continue;
                }

                var serverPassenger = FrameMath.Compose(server, FrameMath.FromFloat(player.Position, player.BodyRotation)).Position;
                var clientPassenger = FrameMath.Compose(seen, FrameMath.FromFloat(local.Position, local.Rotation)).Position;
                var error = Math.Max(Vector3D.Distance(server.Position, seen.Position), Vector3D.Distance(serverPassenger, clientPassenger));
                _errors.Add(error);
                MaxError = Math.Max(MaxError, error);
                if (_last.TryGetValue(client, out var last))
                {
                    var jump = Vector3D.Distance(clientPassenger - last.Client, serverPassenger - last.Server);
                    MaxJump = Math.Max(MaxJump, jump);
                }

                _last[client] = (clientPassenger, serverPassenger);
            }
        }

        public string Report()
        {
            var sorted = _errors.OrderBy(e => e).ToArray();
            var p99 = sorted.Length == 0 ? 0 : sorted[(int)Math.Min(sorted.Length - 1, Math.Ceiling(sorted.Length * 0.99) - 1)];
            return string.Create(CultureInfo.InvariantCulture,
                $"in flight: samples={Samples} missing={Missing} maxError={MaxError:F4} m p99={p99:F4} m maxJumpExcess={MaxJump:F4} m");
        }
    }
}
