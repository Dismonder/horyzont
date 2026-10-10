using System.Numerics;

using Horyzont.Client.Movement;
using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Movement;

public sealed class OwnStateValidatorTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(PredictionBuffer.Capacity)]
    public void Reconcile_AsymmetricInputLoss_AcceptsNeutralServerStepsWithUnchangedAck(int pending)
    {
        var validator = Validator(sent: 200);
        var buffer = new PredictionBuffer(new CorrectionPolicy(), NullLogger<PredictionBuffer>.Instance);
        var airborne = new PredictedState(new Vector3(4020f, 0f, 0f), -Vector3.UnitX, Quaternion.Identity, 0f, MovementMode.Falling, false);
        buffer.Record(1, MotorInput.Neutral(0f, 0f), airborne);
        Assert.Equal(AckOutcome.InSync, buffer.Acknowledge(1, airborne));
        for (uint seq = 2; seq <= pending + 1; seq++)
        {
            Assert.True(buffer.Record(seq, MotorInput.Neutral(0f, 0f), airborne));
        }

        // Client inputs are lost, but server snapshots arrive every three ticks.
        for (uint id = 10; id < 15; id++)
        {
            var elapsed = (id - 9) * 3f / 60f;
            var feet = airborne.Feet - (Vector3.UnitX * (elapsed + (4.905f * elapsed * elapsed)));
            var velocity = -Vector3.UnitX * (1f + (9.81f * elapsed));
            var snapshot = Snap(id: id, tick: 100 + ((id - 9) * 3), ack: 1,
                entities: State(position: feet, velocity: velocity, motion: (byte)MovementMode.Falling));
            Assert.Equal(OwnStateVerdict.Accepted, validator.Validate(snapshot, out var own));
            Assert.Equal(AckOutcome.Correct, buffer.Reconcile(own, 0f, false));
            Assert.Equal(pending, buffer.Count);
        }
    }

    private static readonly EntityId _own = new(7);
    private static readonly PlanetId _planet = new(1);
    private static readonly PlanetId[] _planets = [new PlanetId(1), new PlanetId(2)];

    private static EntityState State(
        EntityId? entity = null, EntityStateFields fields = (EntityStateFields)15, byte frameIndex = 0, Vector3? position = null,
        Quaternion? rotation = null, Vector3? velocity = null, byte motion = (byte)MovementMode.Walking) =>
        new(entity ?? _own, fields, frameIndex, 0, position ?? new Vector3(4000f, 0f, 0f), rotation ?? Quaternion.Identity, velocity ?? Vector3.Zero, motion);

    private static Snapshot Snap(
        uint id = 10, ulong tick = 100, uint ack = 0, uint baseline = 0, SnapshotFrame[]? frames = null, params EntityState[] entities) =>
        new(id, new Tick(tick), baseline, ack, frames ?? [new SnapshotFrame(FrameRef.Of(_planet), 0)], entities.Length == 0 ? [State()] : entities);

    private static OwnStateValidator Validator(ILogger<OwnStateValidator>? logger = null, uint sent = 100)
    {
        var validator = new OwnStateValidator(_own, _planets, logger ?? NullLogger<OwnStateValidator>.Instance);
        validator.NoteSent(sent);
        return validator;
    }

    [Fact]
    public void OwnStateValidator_ValidSnapshot_Accepted()
    {
        var validator = Validator();

        var verdict = validator.Validate(Snap(ack: 5, entities: State(velocity: new Vector3(0f, 0f, 3f), motion: (byte)MovementMode.Sprinting)), out var state);

        Assert.Equal(OwnStateVerdict.Accepted, verdict);
        Assert.Equal(_planet, state.Planet);
        Assert.Equal(new Vector3(4000f, 0f, 0f), state.Feet);
        Assert.Equal(new Vector3(0f, 0f, 3f), state.Velocity);
        Assert.Equal(MovementMode.Sprinting, state.Mode);
        Assert.Equal(5u, state.LastProcessedInputSeq);
        Assert.Equal(10u, state.SnapshotId);
        Assert.Equal(new Tick(100), state.ServerTick);
    }

    public static TheoryData<string, OwnStateVerdict> Hostile => new()
    {
        { "NaN position", OwnStateVerdict.NonFinite },
        { "infinite velocity", OwnStateVerdict.NonFinite },
        { "NaN rotation", OwnStateVerdict.NonFinite },
        { "other entity", OwnStateVerdict.MissingOwnEntity },
        { "frame index out of range", OwnStateVerdict.FrameIndexOutOfRange },
        { "unknown planet", OwnStateVerdict.UnknownPlanet },
        { "system frame", OwnStateVerdict.UnknownPlanet },
        { "rotation too long", OwnStateVerdict.BadRotation },
        { "rotation zero", OwnStateVerdict.BadRotation },
        { "ack beyond sent", OwnStateVerdict.AckBeyondSent },
        { "delta snapshot", OwnStateVerdict.MissingFields },
        { "missing velocity", OwnStateVerdict.MissingFields },
    };

    [Theory]
    [MemberData(nameof(Hostile))]
    public void OwnStateValidator_HostileSnapshots_Rejected(string name, OwnStateVerdict expected)
    {
        var logger = new ListLogger<OwnStateValidator>();
        var validator = Validator(logger);
        var snapshot = name switch
        {
            "NaN position" => Snap(entities: State(position: new Vector3(float.NaN, 0f, 0f))),
            "infinite velocity" => Snap(entities: State(velocity: new Vector3(0f, float.PositiveInfinity, 0f))),
            "NaN rotation" => Snap(entities: State(rotation: new Quaternion(float.NaN, 0f, 0f, 1f))),
            "other entity" => Snap(entities: State(entity: new EntityId(8))),
            "frame index out of range" => Snap(entities: State(frameIndex: 1)),
            "unknown planet" => Snap(frames: [new SnapshotFrame(FrameRef.Of(new PlanetId(99)), 0)]),
            "system frame" => Snap(frames: [new SnapshotFrame(FrameRef.Of(new SystemId(1)), 0)]),
            "rotation too long" => Snap(entities: State(rotation: new Quaternion(0f, 0f, 0f, 1.01f))),
            "rotation zero" => Snap(entities: State(rotation: new Quaternion(0f, 0f, 0f, 0f))),
            "ack beyond sent" => Snap(ack: 101),
            "delta snapshot" => Snap(baseline: 9),
            "missing velocity" => Snap(entities: State(fields: EntityStateFields.Position | EntityStateFields.Rotation | EntityStateFields.MotionFlags)),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        var verdict = validator.Validate(snapshot, out var state);

        Assert.Equal(expected, verdict);
        Assert.Equal(default, state);
        Assert.Equal(1, validator.RejectedCount);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.EventId == 9103);
    }

    [Fact]
    public void OwnStateValidator_OlderOrRepeatedSnapshot_Rejected()
    {
        var validator = Validator();
        Assert.Equal(OwnStateVerdict.Accepted, validator.Validate(Snap(id: 10, tick: 100), out _));

        Assert.Equal(OwnStateVerdict.Stale, validator.Validate(Snap(id: 9, tick: 101), out _));
        Assert.Equal(OwnStateVerdict.Stale, validator.Validate(Snap(id: 10, tick: 101), out _));
        Assert.Equal(OwnStateVerdict.Stale, validator.Validate(Snap(id: 11, tick: 99), out _));
        Assert.Equal(OwnStateVerdict.Accepted, validator.Validate(Snap(id: 11, tick: 100), out _));
    }

    [Fact]
    public void OwnStateValidator_AcknowledgementGoingBackwards_Rejected()
    {
        var validator = Validator();
        Assert.Equal(OwnStateVerdict.Accepted, validator.Validate(Snap(id: 10, ack: 50), out _));

        Assert.Equal(OwnStateVerdict.AckRegressed, validator.Validate(Snap(id: 11, ack: 49), out _));
        Assert.Equal(OwnStateVerdict.Accepted, validator.Validate(Snap(id: 11, ack: 50), out _));
    }

    [Fact]
    public void OwnStateValidator_RejectedSnapshot_DoesNotAdvanceTheHistory()
    {
        var validator = Validator();
        Assert.Equal(OwnStateVerdict.NonFinite, validator.Validate(Snap(id: 500, entities: State(position: new Vector3(float.NaN, 0f, 0f))), out _));

        Assert.Equal(OwnStateVerdict.Accepted, validator.Validate(Snap(id: 10), out _));
    }

    [Fact]
    public void OwnStateValidator_UnknownMotionFlags_AreFallingAndLogged()
    {
        var logger = new ListLogger<OwnStateValidator>();
        var validator = Validator(logger);

        var verdict = validator.Validate(Snap(entities: State(motion: 9)), out var state);

        Assert.Equal(OwnStateVerdict.Accepted, verdict);
        Assert.Equal(MovementMode.Falling, state.Mode);
        Assert.Contains(logger.Entries, e => e.EventId == 9104);
    }

    [Fact]
    public void OwnStateValidator_UnknownMotionFlagsLog_IsRateLimited()
    {
        var logger = new ListLogger<OwnStateValidator>();
        var validator = Validator(logger);

        for (var i = 0; i < 100; i++)
        {
            var verdict = validator.Validate(Snap(id: (uint)(10 + i), tick: (ulong)(100 + i), entities: State(motion: 9)), out var state);
            Assert.Equal(OwnStateVerdict.Accepted, verdict);
            Assert.Equal(MovementMode.Falling, state.Mode);
        }

        // The first one and every 32nd: 1, 33, 65, 97.
        Assert.Equal(4, logger.Entries.Count(e => e.EventId == 9104));
    }

    [Fact]
    public void OwnStateValidator_RejectionLog_IsRateLimited()
    {
        var logger = new ListLogger<OwnStateValidator>();
        var validator = Validator(logger);

        for (var i = 0; i < 100; i++)
        {
            validator.Validate(Snap(entities: State(position: new Vector3(float.NaN, 0f, 0f))), out _);
        }

        Assert.Equal(100, validator.RejectedCount);
        Assert.Equal(4, logger.Entries.Count(e => e.EventId == 9103));
    }

    [Fact]
    public void OwnStateValidator_Reset_ForgetsTheHistory()
    {
        var validator = Validator();
        validator.Validate(Snap(id: 10, ack: 50), out _);

        validator.Reset();
        validator.NoteSent(100);

        Assert.Equal(OwnStateVerdict.Accepted, validator.Validate(Snap(id: 1, tick: 5, ack: 0), out _));
    }

    [Fact]
    public void OwnStateValidator_Accepted_ConvertsToPredictedState()
    {
        var validator = Validator();
        validator.Validate(Snap(entities: State(velocity: Vector3.UnitZ, motion: (byte)MovementMode.Jumping)), out var state);

        var predicted = state.ToPredicted(0.3f, onFloor: false);

        Assert.Equal(new PredictedState(state.Feet, Vector3.UnitZ, Quaternion.Identity, 0.3f, MovementMode.Jumping, false), predicted);
    }

    [Fact]
    public void OwnStateValidator_Accepting_DoesNotAllocate()
    {
        var validator = Validator();
        var snapshots = new Snapshot[64];
        for (var i = 0; i < snapshots.Length; i++)
        {
            snapshots[i] = Snap(id: (uint)(i + 1), tick: (ulong)(i + 1));
        }

        for (var round = 0; round < 3; round++)
        {
            validator.Reset();
            validator.NoteSent(100);
            foreach (var snapshot in snapshots)
            {
                validator.Validate(snapshot, out _);
            }
        }

        validator.Reset();
        validator.NoteSent(100);
        var before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var snapshot in snapshots)
        {
            validator.Validate(snapshot, out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void OwnStateValidator_FromHello_UsesEntityAndPlanets()
    {
        var hello = new ServerHello(
            new WorldId(Guid.NewGuid()), new Tick(0), 60, new PlayerId(1), _own,
            [new Horyzont.Core.World.PlanetParams(_planet, new SystemId(1), 1, "core:test/1")], [], []);

        var validator = OwnStateValidator.FromHello(hello, NullLogger<OwnStateValidator>.Instance);

        Assert.Equal(OwnStateVerdict.Accepted, validator.Validate(Snap(), out _));
        Assert.Equal(OwnStateVerdict.UnknownPlanet, validator.Validate(Snap(id: 11, frames: [new SnapshotFrame(FrameRef.Of(new PlanetId(2)), 0)]), out _));
    }
}
