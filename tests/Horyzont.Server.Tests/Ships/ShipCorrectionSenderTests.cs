using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Server.Replication;
using Horyzont.Server.Ships;
using Horyzont.Server.Tests.Support;

namespace Horyzont.Server.Tests.Ships;

/// <summary>K5-25 decisions 14 and 15 (R2, R11, Y5): the ship correction of the pilot.</summary>
public sealed class ShipCorrectionSenderTests
{
    private static readonly ShipControl _climb = new(0, 127, 0, 0, 0, 0, ShipButtons.Assist);

    private static (SeatRig Rig, PlayerId Pilot, ShipCorrectionSender Sender) Flying(IShipTerrainGate? gate = null)
    {
        var rig = new SeatRig(guard: false, gate: gate);
        var pilot = rig.Join(1);
        rig.Sit(pilot, SeatRig.Cockpit);
        for (uint s = 1; s <= 40; s++)
        {
            rig.Inputs.Add(pilot, s, _climb);
            rig.Ticks(1);
        }

        return (rig, pilot, new ShipCorrectionSender(rig.Flight.Sim, () => rig.Flight.Flight, () => rig.Pilot));
    }

    [Fact]
    public void ShipCorrectionSender_StateAfterStepOfLastProcessedSeq()
    {
        var (rig, pilot, sender) = Flying();
        var frame = new byte[ShipCorrectionWriter.MaxBytes];

        var size = sender.Write(pilot, frame, out var ship);

        Assert.True(size > 0);
        Assert.Equal(FlightRig.Ship, ship);
        var correction = Assert.IsType<ShipCorrection>(MessageCodec.Read(frame.AsSpan(0, size)));
        Assert.True(rig.Pilot.TryGetLastProcessedSeq(FlightRig.Ship, out _, out var seq));
        Assert.Equal(seq, correction.LastProcessedSeq);
        Assert.True(rig.Flight.Flight.TryGetView(FlightRig.Ship, out var view));
        Assert.True(FrameMath.TryToFloat(view.Motion.Pose, view.Parent.Kind, out var position, out var rotation));
        var expected = new ShipCorrection(
            FlightRig.Ship, seq, rig.Flight.Construct().ParentSeq, position, rotation, view.Motion.LinearVelocity, view.Motion.AngularVelocity,
            view.Motion.Fuel, view.Motion.Flags, view.Environment.Gate, view.Environment.LaunchBlock, (float)view.Environment.HorizontalSpeedCap);
        Assert.Equal(expected, correction);
        Assert.Equal(MessageCodecBytes(expected), frame.AsSpan(0, size).ToArray());
        Assert.True(correction.LinearVelocity.Length() > 0.5f);
    }

    [Fact]
    public void ShipCorrectionSender_OnlyToPilot_WithPilotSnapshot()
    {
        var (rig, _, sender) = Flying();
        var passenger = rig.Join(2);
        rig.Sit(passenger, SeatRig.SeatA);
        var standing = rig.Join(3);
        var frame = new byte[ShipCorrectionWriter.MaxBytes];

        Assert.Equal(0, sender.Write(passenger, frame, out _));
        Assert.Equal(0, sender.Write(standing, frame, out _));
        Assert.Equal(0, new ShipCorrectionSender(rig.Flight.Sim, () => null, () => rig.Pilot).Write(passenger, frame, out _));
    }

    [Fact]
    public void ShipCorrectionSender_RoundAllocatesZero()
    {
        var (_, pilot, sender) = Flying();
        var frame = new byte[ShipCorrectionWriter.MaxBytes];
        for (var i = 0; i < 100; i++)
        {
            sender.Write(pilot, frame, out _);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            sender.Write(pilot, frame, out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void ShipFlightSystem_HorizontalCapRoundedToFloat()
    {
        const double cap = 123.456789012345;
        var (rig, _, _) = Flying(new CapGate(cap));

        Assert.True(rig.Flight.Flight.TryGetView(FlightRig.Ship, out var view));
        Assert.Equal((double)(float)cap, view.Environment.HorizontalSpeedCap);
    }

    [Fact]
    public async Task ShipCorrectionSender_DebitsSnapshotBudget()
    {
        var budget = new CountingBudget();
        await using var rig = await ReplicationRig.StartAsync(snapshotBudget: budget);
        var alice = await rig.JoinAsync("alice", 1);
        var ship = rig.AddShip(new Vector3(0, 4010, 0));
        rig.Step(30);
        var source = new FixedCorrection(alice.Player, ship);
        rig.Replication.ShipCorrections = source;
        budget.Bytes = 0;
        alice.Received.Clear();

        rig.Step(30);

        var corrections = alice.Received.OfType<ShipCorrection>().ToList();
        var snapshots = alice.Received.OfType<Snapshot>().Count();
        Assert.NotEmpty(corrections);
        Assert.InRange(corrections.Count, snapshots - 1, snapshots + 1);
        Assert.True(budget.Bytes >= corrections.Count * source.Size, $"{budget.Bytes} B debited");
    }

    private static byte[] MessageCodecBytes(ShipCorrection message)
    {
        var writer = new ByteWriter();
        MessageCodec.Write(writer, message);
        return writer.WrittenSpan.ToArray();
    }

    private sealed class CapGate(double cap) : IShipTerrainGate
    {
        public ShipTerrainGate Query(EntityId ship, PlanetId planet, in ShipMotion motion, ShipStats stats, out double horizontalSpeedCap)
        {
            horizontalSpeedCap = cap;
            return ShipTerrainGate.Ready;
        }

        public ShipTerrainGate QueryGround(EntityId ship, PlanetId planet, in ShipMotion motion, ShipStats stats, double depthMeters) => ShipTerrainGate.Ready;

        public void Forget(EntityId ship)
        {
        }
    }

    private sealed class CountingBudget : ISnapshotBudget
    {
        public long Bytes { get; set; }

        public int GetByteBudget(PlayerId player, Tick tick) => 1150;

        public void Debit(PlayerId player, int encodedBytes) => Bytes += encodedBytes;
    }

    private sealed class FixedCorrection(PlayerId pilot, EntityId ship) : IShipCorrectionSource
    {
        public int Size { get; private set; }

        public int Write(PlayerId player, Span<byte> destination, out EntityId correctionShip)
        {
            correctionShip = ship;
            if (player != pilot)
            {
                return 0;
            }

            Size = ShipCorrectionWriter.Write(
                destination, ship, 7, 0, new Vector3(0, 4010, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero, 10f, ShipFlightFlags.Assist,
                ShipTerrainGate.Ready, ShipLaunchBlock.None, float.PositiveInfinity);
            return Size;
        }
    }
}
