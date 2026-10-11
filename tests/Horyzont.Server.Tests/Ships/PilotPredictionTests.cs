using System.Globalization;

using Horyzont.Client.Ships;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Server.Ships;
using Horyzont.Testing.Ships;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests.Ships;

/// <summary>
/// K5-25 measurement 2 (criterion 2): the client pilot prediction against the real server flight (<see cref="SeatRig"/>:
/// <see cref="ShipFlightSystem"/>, <see cref="PilotControlSource"/>, <see cref="ShipCorrectionSender"/>, <see cref="SphereShipCollider"/>)
/// over a link with RTT 100 ± 10 ms and losses; a gate with a Y11 band (horizontal cap) near the ground.
/// </summary>
public sealed class PilotPredictionTests(ITestOutputHelper output)
{
    private const double TickMs = 1000.0 / FlightRig.TickRate;
    private const int SnapshotEvery = 3;

    [Fact]
    public void PilotPrediction_NoLatency_SameInputsAsServer_ZeroCorrections()
    {
        var report = Run(seed: 7, seconds: 40, rttMs: 0, jitterMs: 0, loss: 0);

        Assert.Equal(0, report.Corrections);
        Assert.Equal(0, report.Dropped);
        Assert.True(report.Errors.Count > 300, $"{report.Errors.Count} corrections compared");
        Assert.True(report.Errors.Max() < 1e-3, $"max {report.Errors.Max()}");
    }

    [Theory]
    [Trait("Category", "Slow")]
    [InlineData(11, 0.05)]
    [InlineData(29, 0.05)]
    [InlineData(11, 0.0)]
    public void PilotPrediction_Rtt100_RespondsSameTick_CorrectionsBelow05mIn95Percent(int seed, double loss)
    {
        var report = Run(seed, seconds: 180, rttMs: 100, jitterMs: 10, loss: loss);
        var line = report.Describe(seed, loss);
        output.WriteLine(line);
        if (Environment.GetEnvironmentVariable("HORYZONT_MEASURE_OUT") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }

        Assert.Equal(report.StepChanges, report.StepResponded);
        Assert.True(report.StepChanges >= 10, line);
        Assert.True(report.Percentile(0.95) < 0.5, line);
        Assert.Equal(0, report.Snaps);
        Assert.True(report.Errors.Count > 2000, line);
    }

    private static Report Run(int seed, int seconds, double rttMs, double jitterMs, double loss)
    {
        var random = new Random(seed);
        var rig = new SeatRig(gate: new BandGate());
        var pilot = rig.Join(1);
        rig.Sit(pilot, SeatRig.Cockpit);
        rig.Ticks(2);
        var sender = new ShipCorrectionSender(rig.Flight.Sim, () => rig.Flight.Flight, () => rig.Pilot);
        Assert.True(rig.Flight.Flight.TryGetView(FlightRig.Ship, out var start));
        Assert.True(rig.Flight.Stats.TryGet(FlightRig.Ship, out var stats));
        var source = ClientFlightEnvironment.InPlanet(FlightFixtures.Home, 0, FlightRules.Default);
        var prediction = new ShipPrediction(
            source,
            (in PoseD from, in PoseD to) => rig.Flight.Collider.Sweep(FlightRig.Ship, from, to),
            FlightRules.Default,
            FlightRig.TickRate,
            NullLogger.Instance);
        prediction.Reset(start.Motion, rig.Flight.Construct().ParentSeq, stats);
        var sequencer = new ShipInputSequencer();
        var toServer = new List<(double At, ShipInput Input, uint Seq)>();
        var toClient = new List<(double At, ShipCorrection Correction)>();
        var frame = new byte[ShipCorrectionWriter.MaxBytes];
        var writer = new ByteWriter();
        var report = new Report();
        var previous = default(ShipControl);
        double Delay() => (rttMs / 2) + ((random.NextDouble() * 2) - 1) * (jitterMs / 2);

        var ticks = seconds * FlightRig.TickRate;
        for (var t = 0; t < ticks; t++)
        {
            var now = t * TickMs;

            // Client tick: input, prediction (responds in this very step), batch.
            var control = Script(t, random);
            var seq = sequencer.Record((uint)t, control);
            var before = prediction.Motion;
            prediction.Step(seq, control);
            if (control != previous && (before.Flags & ShipFlightFlags.Parked) == 0)
            {
                // Every change counts for the report; the script steps (not the seeded wobble, which a saturated axis may ignore) must respond.
                var step = Segment(t) != Segment(t - 1);
                report.InputChanges++;
                report.StepChanges += step ? 1 : 0;
                var counterfactual = before;
                var environment = ClientFlightEnvironment.Complete(
                    source(counterfactual, stats), ref counterfactual, stats, FlightRules.Default, ShipTerrainGate.Ready, double.PositiveInfinity, false, previous);
                ShipFlightModel.Step(ref counterfactual, previous, stats, environment, FlightRules.Default, 1f / FlightRig.TickRate);
                if (counterfactual.LinearVelocity != prediction.Motion.LinearVelocity || counterfactual.AngularVelocity != prediction.Motion.AngularVelocity)
                {
                    report.Responded++;
                    report.StepResponded += step ? 1 : 0;
                }
            }

            previous = control;
            while (sequencer.TryTakeBatch(out var first, out var inputs))
            {
                writer.Reset();
                MessageCodec.Write(writer, new ShipInputBatch(first, inputs.ToArray()));
                report.UpBytes += writer.Length;
                if (random.NextDouble() < loss)
                {
                    continue;
                }

                var at = now + Delay();
                for (var i = 0; i < inputs.Count; i++)
                {
                    toServer.Add((at, inputs[i], first + (uint)i));
                }
            }

            // Server tick: inputs that arrived, flight step, a correction with every third (pilot) snapshot.
            Deliver(toServer, now, item => rig.Inputs.Add(pilot, item.Seq, item.Input.Control));
            rig.Ticks(1);
            if (t % SnapshotEvery == 0)
            {
                var size = sender.Write(pilot, frame, out _);
                if (size > 0 && random.NextDouble() >= loss)
                {
                    toClient.Add((now + Delay(), (ShipCorrection)MessageCodec.Read(frame.AsSpan(0, size))));
                }
            }

            // Client receive.
            Deliver(toClient, now + TickMs, item =>
            {
                var dropped = prediction.Dropped;
                prediction.Apply(item.Correction);
                if (prediction.Dropped == dropped && !prediction.HasHeld)
                {
                    report.Errors.Add(prediction.LastError);
                }
            });
        }

        report.Corrections = prediction.Corrections;
        report.Snaps = prediction.Snaps;
        report.Dropped = prediction.Dropped;
        report.Substituted = rig.Pilot.Stats.Substituted;
        report.Reanchors = rig.Pilot.Stats.Reanchors;
        report.Seconds = seconds;
        report.Final = prediction.Motion.Flags;
        return report;
    }

    private static void Deliver<T>(List<T> queue, double now, Action<T> deliver)
        where T : struct
    {
        var due = new List<T>();
        for (var i = queue.Count - 1; i >= 0; i--)
        {
            if (At(queue[i]) <= now)
            {
                due.Add(queue[i]);
                queue.RemoveAt(i);
            }
        }

        due.Sort((a, b) => At(a).CompareTo(At(b)));
        foreach (var item in due)
        {
            deliver(item);
        }

        static double At(T item) => item switch
        {
            ValueTuple<double, ShipInput, uint> input => input.Item1,
            ValueTuple<double, ShipCorrection> correction => correction.Item1,
            _ => throw new InvalidOperationException(),
        };
    }

    private static int Segment(int tick) => Array.FindIndex(_segmentEnds, end => tick < end * FlightRig.TickRate);

    private static readonly int[] _segmentEnds = [2, 12, 18, 24, 30, 34, 42, 50, 80, 100, 110, 170, int.MaxValue / 120];

    /// <summary>
    /// 180 s: parked, climb, rotations in every axis, lateral thrust, 30 s of pulse drive, braking by the speed limit, hover, descent with
    /// the gear down to the parking. Small seeded variations of the axes.
    /// </summary>
    private static ShipControl Script(int tick, Random random)
    {
        var s = tick / (double)FlightRig.TickRate;
        sbyte Wobble(int value) => (sbyte)Math.Clamp(value + random.Next(-3, 4), -127, 127);
        var assist = ShipButtons.Assist;
        return s switch
        {
            < 2 => new ShipControl(0, 0, 0, 0, 0, 0, assist | ShipButtons.LandingGear),
            < 12 => new ShipControl(0, Wobble(110), 0, 0, 0, 0, assist),
            < 18 => new ShipControl(0, 30, 0, Wobble(60), 0, 0, assist),
            < 24 => new ShipControl(0, 30, 0, 0, Wobble(-70), 0, assist),
            < 30 => new ShipControl(0, 30, 0, 0, 0, Wobble(80), assist),
            < 34 => new ShipControl(0, 0, 0, Wobble(-60), Wobble(70), Wobble(-80), assist),
            < 42 => new ShipControl(Wobble(100), 0, 0, 0, 0, 0, assist),
            < 50 => new ShipControl(Wobble(-100), 20, Wobble(-90), 0, 0, 0, assist),
            < 80 => new ShipControl(0, 10, Wobble(-120), 0, 0, 0, assist | ShipButtons.PulseDrive),
            < 100 => new ShipControl(0, 0, 0, 0, 0, 0, assist),
            < 110 => new ShipControl(0, 0, 0, 0, (sbyte)(tick % 120 < 60 ? 20 : -20), 0, assist),
            < 170 => new ShipControl(0, Wobble(-127), 0, 0, 0, 0, assist | ShipButtons.LandingGear),
            _ => new ShipControl(0, -40, 0, 0, 0, 0, assist | ShipButtons.LandingGear),
        };
    }

    /// <summary>Ready terrain with a Y11 band: within 80 m of the ground the horizontal speed is capped at 20 m/s.</summary>
    private sealed class BandGate : IShipTerrainGate
    {
        public ShipTerrainGate Query(EntityId ship, PlanetId planet, in ShipMotion motion, ShipStats stats, out double horizontalSpeedCap)
        {
            var height = ClientFlightEnvironment.CenterOfMass(motion, stats).Length() - FlightFixtures.Home.RadiusMeters - stats.BoundingRadius;
            horizontalSpeedCap = height < 80 ? 20 : double.PositiveInfinity;
            return ShipTerrainGate.Ready;
        }

        public ShipTerrainGate QueryGround(EntityId ship, PlanetId planet, in ShipMotion motion, ShipStats stats, double depthMeters) => ShipTerrainGate.Ready;

        public void Forget(EntityId ship)
        {
        }
    }

    private sealed class Report
    {
        public List<double> Errors { get; } = [];

        public int InputChanges { get; set; }

        public int Responded { get; set; }

        public int StepChanges { get; set; }

        public int StepResponded { get; set; }

        public int Corrections { get; set; }

        public int Snaps { get; set; }

        public int Dropped { get; set; }

        public long Substituted { get; set; }

        public long Reanchors { get; set; }

        public long UpBytes { get; set; }

        public int Seconds { get; set; }

        public ShipFlightFlags Final { get; set; }

        public double Percentile(double p)
        {
            var sorted = Errors.Order().ToList();
            return sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];
        }

        public string Describe(int seed, double loss) => string.Create(
            CultureInfo.InvariantCulture,
            $"PilotPrediction seed={seed} loss={loss:P0}: n={Errors.Count} p50={Percentile(0.5):F4} p95={Percentile(0.95):F4} p99={Percentile(0.99):F4} max={(Errors.Count == 0 ? 0 : Errors.Max()):F4} m; corrections={Corrections} snaps={Snaps} dropped={Dropped} responded={StepResponded}/{StepChanges} steps, {Responded}/{InputChanges} all substituted={Substituted} reanchors={Reanchors} up={UpBytes / (double)Seconds:F0} B/s final={Final}");
    }
}
