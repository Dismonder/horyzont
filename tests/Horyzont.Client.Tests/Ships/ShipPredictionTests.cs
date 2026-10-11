using System.Numerics;

using Horyzont.Client.Ships;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Testing.Ships;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Tests.Ships;

/// <summary>K5-25 decisions 16 and 17: the pilot prediction, its reconciliation and the render offset.</summary>
public sealed class ShipPredictionTests
{
    private const int TickRate = 60;
    private static readonly ShipControl _hover = new(0, 0, 0, 0, 0, 0, ShipButtons.Assist);
    private static readonly ShipControl _climb = new(0, 127, 40, 0, 20, 0, ShipButtons.Assist);

    private static ShipPrediction Create(ShipStats stats, LogLines? log = null)
    {
        var ground = new SphereGroundCollider().Add(Vector3D.Zero, FlightFixtures.Home.RadiusMeters);
        var prediction = new ShipPrediction(
            ClientFlightEnvironment.InPlanet(FlightFixtures.Home, 0, FlightRules.Default),
            (in PoseD from, in PoseD to) => ground.Sweep(from, to, stats),
            FlightRules.Default,
            TickRate,
            log ?? new LogLines());
        prediction.Reset(FlightFixtures.AboveHome(80), 0, stats);
        return prediction;
    }

    private static ShipCorrection From(uint seq, in ShipMotion m, Vector3 offset = default)
    {
        FrameMath.TryToFloat(m.Pose, FrameKind.Planet, out var position, out var rotation);
        return new ShipCorrection(
            new EntityId(1), seq, 0, position + offset, rotation, m.LinearVelocity, m.AngularVelocity, m.Fuel, m.Flags, ShipTerrainGate.Ready,
            ShipLaunchBlock.None, float.PositiveInfinity);
    }

    [Fact]
    public void ShipPrediction_SameInputsAsServer_ZeroCorrections()
    {
        var stats = FlightFixtures.Thirty();
        var client = Create(stats);
        var server = Create(stats);
        for (uint seq = 1; seq <= 300; seq++)
        {
            var control = seq % 50 < 25 ? _climb : _hover;
            client.Step(seq, control);
            server.Step(seq, control);
            if (seq % 3 == 0 && seq > 6)
            {
                // The server state of a slot six inputs back, as a correction arrives one RTT later.
                var twin = Create(stats);
                for (uint s = 1; s <= seq - 6; s++)
                {
                    twin.Step(s, s % 50 < 25 ? _climb : _hover);
                }

                Assert.False(client.Apply(From(seq - 6, twin.Motion)));
            }
        }

        Assert.Equal(0, client.Corrections);
        Assert.Equal(server.Motion.Pose, client.Motion.Pose);
    }

    [Fact]
    public void ShipPrediction_RespondsSameTick()
    {
        var prediction = Create(FlightFixtures.Thirty());
        prediction.Step(1, _hover);
        var before = prediction.Motion.LinearVelocity;

        prediction.Step(2, _climb);

        Assert.NotEqual(before, prediction.Motion.LinearVelocity);
    }

    [Fact]
    public void ShipPrediction_ErrorBelow5m_SmoothedOver150ms()
    {
        var prediction = Create(FlightFixtures.Thirty());
        for (uint seq = 1; seq <= 20; seq++)
        {
            prediction.Step(seq, _hover);
        }

        var shown = prediction.RenderPose().Position;
        var twin = Create(FlightFixtures.Thirty());
        for (uint seq = 1; seq <= 14; seq++)
        {
            twin.Step(seq, _hover);
        }

        Assert.True(prediction.Apply(From(14, twin.Motion, new Vector3(1f, 0f, 0f))));

        Assert.True(Vector3D.Distance(shown, prediction.RenderPose().Position) < 1e-6, "no visible jump");
        Assert.InRange(Vector3D.Distance(prediction.Motion.Pose.Position, shown), 0.9, 1.1);
        for (uint seq = 21; seq <= 29; seq++)
        {
            prediction.Step(seq, _hover);
        }

        Assert.Equal(prediction.Motion.Pose, prediction.RenderPose());
        Assert.Equal(0, prediction.Snaps);
        Assert.Equal(1, prediction.Corrections);
    }

    [Fact]
    public void ShipPrediction_ErrorOver5m_SnapsAndLogsLastInputs()
    {
        var log = new LogLines();
        var prediction = Create(FlightFixtures.Thirty(), log);
        for (uint seq = 1; seq <= 20; seq++)
        {
            prediction.Step(seq, _hover);
        }

        var twin = Create(FlightFixtures.Thirty());
        for (uint seq = 1; seq <= 14; seq++)
        {
            twin.Step(seq, _hover);
        }

        Assert.True(prediction.Apply(From(14, twin.Motion, new Vector3(8f, 0f, 0f))));

        Assert.Equal(1, prediction.Snaps);
        Assert.Equal(prediction.Motion.Pose, prediction.RenderPose());
        Assert.Contains(log.Lines, l => l.StartsWith("Warning", StringComparison.Ordinal) && l.Contains("snapped", StringComparison.Ordinal));
    }

    [Fact]
    public void ShipPrediction_OlderParentSeq_Dropped()
    {
        var prediction = Create(FlightFixtures.Thirty());
        prediction.Step(1, _hover);
        prediction.Rebase(2, Vector3D.Zero);

        Assert.False(prediction.Apply(From(1, prediction.Motion, new Vector3(3f, 0f, 0f)) with { ParentSeq = 1 }));
        Assert.Equal(1, prediction.Dropped);
        Assert.False(prediction.HasHeld);
    }

    [Fact]
    public void ShipPrediction_NewerParentSeq_HeldUntilReparent()
    {
        var prediction = Create(FlightFixtures.Thirty());
        for (uint seq = 1; seq <= 20; seq++)
        {
            prediction.Step(seq, _hover);
        }

        var twin = Create(FlightFixtures.Thirty());
        for (uint seq = 1; seq <= 14; seq++)
        {
            twin.Step(seq, _hover);
        }

        Assert.False(prediction.Apply(From(14, twin.Motion, new Vector3(2f, 0f, 0f)) with { ParentSeq = 1 }));
        Assert.True(prediction.HasHeld);
        Assert.Equal(0, prediction.Dropped);

        prediction.Rebase(1, Vector3D.Zero);

        Assert.False(prediction.HasHeld);
        Assert.Equal(1, prediction.Corrections);
        Assert.Equal(1u, prediction.ParentSeq);
    }

    [Fact]
    public void ShipPrediction_NewerParentSeq_DroppedAfterOneSecond()
    {
        var prediction = Create(FlightFixtures.Thirty());
        prediction.Step(1, _hover);
        prediction.Apply(From(1, prediction.Motion) with { ParentSeq = 1 });
        for (uint seq = 2; seq <= TickRate + 1; seq++)
        {
            prediction.Step(seq, _hover);
            Assert.True(prediction.HasHeld);
        }

        prediction.Step(TickRate + 2, _hover);

        Assert.False(prediction.HasHeld);
        Assert.Equal(1, prediction.HeldExpired);
        Assert.Equal(1, prediction.Dropped);
    }

    [Fact]
    public void ShipPrediction_ZoneTransition_RingRebased()
    {
        var prediction = Create(FlightFixtures.Thirty());
        var twin = Create(FlightFixtures.Thirty());
        for (uint seq = 1; seq <= 20; seq++)
        {
            prediction.Step(seq, _climb);
            if (seq <= 14)
            {
                twin.Step(seq, _climb);
            }
        }

        var offset = new Vector3D(250, -40, 10);
        var before = prediction.Motion.Pose.Position;
        prediction.Rebase(1, offset, new Vector3(1f, 0f, 0f));

        Assert.Equal(before + offset, prediction.Motion.Pose.Position);
        var server = twin.Motion;
        server.LinearVelocity += new Vector3(1f, 0f, 0f);
        Assert.False(prediction.Apply(From(14, server, new Vector3(250f, -40f, 10f)) with { ParentSeq = 1 }));
        Assert.Equal(0, prediction.Corrections);
        Assert.Equal(0, prediction.Dropped);
    }

    [Fact]
    public void ShipPrediction_StepAllocatesZero()
    {
        var prediction = Create(FlightFixtures.Thirty());
        uint seq = 1;
        for (; seq <= 200; seq++)
        {
            prediction.Step(seq, _climb);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (; seq <= 1200; seq++)
        {
            prediction.Step(seq, seq % 2 == 0 ? _climb : _hover);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private sealed class LogLines : ILogger
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            Lines.Add($"{logLevel}:{formatter(state, exception)}");
        }
    }
}
