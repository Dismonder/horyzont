using System.Numerics;

using Horyzont.Client.Movement;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Movement;

public sealed class PredictionBufferTests
{
    private static MotorInput InputFor(uint seq) => new(seq / 100f, 1f, 0f, 0f, InputButtons.None);

    /// <summary>The state the client predicts after input <paramref name="seq"/>: 0.1 m per input along X.</summary>
    private static PredictedState StateFor(uint seq) =>
        new(new Vector3(4000f + (0.1f * seq), 0f, 0f), new Vector3(0f, 0f, 6f), Quaternion.Identity, 0f, MovementMode.Walking, true);

    private static PredictionBuffer Filled(uint count, ILogger<PredictionBuffer>? logger = null, CorrectionPolicy? policy = null)
    {
        var buffer = new PredictionBuffer(policy ?? new CorrectionPolicy(), logger ?? NullLogger<PredictionBuffer>.Instance);
        for (uint seq = 1; seq <= count; seq++)
        {
            Assert.True(buffer.Record(seq, InputFor(seq), StateFor(seq)));
        }

        return buffer;
    }

    [Fact]
    public void PredictionBuffer_ServerMatches_NoCorrection()
    {
        var buffer = Filled(5);

        var outcome = buffer.Acknowledge(3, StateFor(3));

        Assert.Equal(AckOutcome.InSync, outcome);
        Assert.Equal(2, buffer.Count);
        Span<PredictionEntry> rest = stackalloc PredictionEntry[8];
        Assert.Equal(2, buffer.CopyUnacknowledged(rest));
        Assert.Equal(4u, rest[0].Seq);
        Assert.Equal(5u, rest[1].Seq);
    }

    [Fact]
    public void PredictionBuffer_ErrorBelowThresholds_StaysInSync()
    {
        var buffer = Filled(3);
        var server = StateFor(2) with { Feet = StateFor(2).Feet + new Vector3(0f, 0.015f, 0f), Velocity = StateFor(2).Velocity + new Vector3(0.05f, 0f, 0f) };

        Assert.Equal(AckOutcome.InSync, buffer.Acknowledge(2, server));
    }

    [Fact]
    public void PredictionBuffer_Mismatch_SnapsAndReplaysUnacknowledged()
    {
        var buffer = Filled(6);
        var server = StateFor(3) with { Feet = StateFor(3).Feet + new Vector3(0f, 0.05f, 0f) };

        var outcome = buffer.Acknowledge(3, server);

        Assert.Equal(AckOutcome.Correct, outcome);
        Span<PredictionEntry> replay = stackalloc PredictionEntry[PredictionBuffer.Capacity];
        var count = buffer.CopyUnacknowledged(replay);
        Assert.Equal(3, count);
        for (var i = 0; i < count; i++)
        {
            Assert.Equal(4u + (uint)i, replay[i].Seq);
            Assert.Equal(InputFor(4u + (uint)i), replay[i].Input);
        }
    }

    [Fact]
    public void PredictionBuffer_VelocityOrModeMismatch_Corrects()
    {
        var velocity = Filled(2).Acknowledge(1, StateFor(1) with { Velocity = new Vector3(0f, 0f, 5.8f) });
        var mode = Filled(2).Acknowledge(1, StateFor(1) with { Mode = MovementMode.Falling });

        Assert.Equal(AckOutcome.Correct, velocity);
        Assert.Equal(AckOutcome.Correct, mode);
    }

    [Fact]
    public void PredictionBuffer_AckWithoutEntry_Corrects()
    {
        // Inputs 1..3 were sent while prediction was paused; the first recorded entry is 4.
        var buffer = new PredictionBuffer(new CorrectionPolicy(), NullLogger<PredictionBuffer>.Instance);
        for (uint seq = 4; seq <= 6; seq++)
        {
            buffer.Record(seq, InputFor(seq), StateFor(seq));
        }

        var outcome = buffer.Acknowledge(2, StateFor(2));

        Assert.Equal(AckOutcome.Correct, outcome);
        Assert.Equal(3, buffer.Count);
    }

    [Theory]
    [InlineData(PredictionBuffer.Capacity + 1)]
    [InlineData(0)]
    [InlineData(-1)]
    public void PredictionBuffer_MaxUnacknowledgedOutsideCapacity_Throws(int max) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PredictionBuffer(new CorrectionPolicy { MaxUnacknowledged = max }, NullLogger<PredictionBuffer>.Instance));

    [Fact]
    public void PredictionBuffer_MaxUnacknowledgedEqualToCapacity_IsAccepted() =>
        Assert.NotNull(new PredictionBuffer(new CorrectionPolicy { MaxUnacknowledged = PredictionBuffer.Capacity }, NullLogger<PredictionBuffer>.Instance));

    [Fact]
    public void PredictionBuffer_AckBeyondNewestEntry_CorrectsAndEmptiesTheBuffer()
    {
        var buffer = Filled(3);

        Assert.Equal(AckOutcome.Correct, buffer.Acknowledge(9, StateFor(9)));

        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void PredictionBuffer_StaleAck_Ignored()
    {
        var buffer = Filled(5);
        buffer.Acknowledge(3, StateFor(3));

        Assert.Equal(AckOutcome.Stale, buffer.Acknowledge(3, StateFor(3) with { Feet = Vector3.Zero }));
        Assert.Equal(AckOutcome.Stale, buffer.Acknowledge(2, StateFor(2)));
        Assert.Equal(2, buffer.Count);
    }

    [Fact]
    public void PredictionBuffer_ReplayedStates_AreWhatTheNextAckIsComparedWith()
    {
        var buffer = Filled(5);
        Assert.Equal(AckOutcome.Correct, buffer.Acknowledge(2, StateFor(2) with { Feet = Vector3.UnitX * 5000f }));
        var replayed = new[] { StateFor(3) with { Feet = Vector3.UnitX * 5000.1f }, StateFor(4) with { Feet = Vector3.UnitX * 5000.2f }, StateFor(5) with { Feet = Vector3.UnitX * 5000.3f } };

        buffer.ReplaceUnacknowledgedStates(replayed);

        Assert.Equal(AckOutcome.InSync, buffer.Acknowledge(4, replayed[1]));
    }

    [Fact]
    public void PredictionBuffer_OverMaxUnacknowledged_PausesPrediction()
    {
        var logger = new ListLogger<PredictionBuffer>();
        var buffer = Filled(119, logger);
        Assert.False(buffer.IsPaused);

        buffer.Record(120, InputFor(120), StateFor(120));
        Assert.True(buffer.IsPaused);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.EventId == 9102);

        // Resumes after the server acknowledges something.
        buffer.Acknowledge(1, StateFor(1));
        Assert.False(buffer.IsPaused);
    }

    [Fact]
    public void PredictionBuffer_FullRing_RefusesToRecord()
    {
        var buffer = Filled(PredictionBuffer.Capacity);

        Assert.False(buffer.Record(PredictionBuffer.Capacity + 1, InputFor(1), StateFor(1)));
        Assert.Equal(PredictionBuffer.Capacity, buffer.Count);
    }

    [Fact]
    public void PredictionBuffer_RingWrapsAroundAcrossManyAcknowledgements()
    {
        var buffer = new PredictionBuffer(new CorrectionPolicy(), NullLogger<PredictionBuffer>.Instance);
        for (uint seq = 1; seq <= 1000; seq++)
        {
            Assert.True(buffer.Record(seq, InputFor(seq), StateFor(seq)));
            if (seq > 3)
            {
                Assert.Equal(AckOutcome.InSync, buffer.Acknowledge(seq - 3, StateFor(seq - 3)));
            }
        }

        Assert.Equal(3, buffer.Count);
    }

    [Fact]
    public void PredictionBuffer_Correction_IsLoggedAtDebug()
    {
        var logger = new ListLogger<PredictionBuffer>();
        var buffer = Filled(3, logger);

        buffer.Acknowledge(2, StateFor(2) with { Feet = Vector3.Zero });

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug && e.EventId == 9101);
    }

    [Fact]
    public void PredictionBuffer_Disconnect_Clears()
    {
        var buffer = Filled(120);
        buffer.Acknowledge(5, StateFor(5));
        Assert.False(buffer.IsPaused);

        buffer.Clear();

        Assert.Equal(0, buffer.Count);
        Assert.False(buffer.IsPaused);

        // The numbering starts again: an acknowledgement of 1 is no longer stale.
        buffer.Record(1, InputFor(1), StateFor(1));
        Assert.Equal(AckOutcome.InSync, buffer.Acknowledge(1, StateFor(1)));
    }

    [Fact]
    public void PredictionBuffer_And_InputSequencer_SteadyState_DoNotAllocate()
    {
        var sequencer = new InputSequencer();
        var buffer = new PredictionBuffer(new CorrectionPolicy(), NullLogger<PredictionBuffer>.Instance);
        var rules = new MovementRules();
        MotorInput.TryFrom(new PlayerInput(0, 0.5f, 1f, 0.3f, 0.1f, InputButtons.Sprint), rules, out var motor);

        void Tick(uint n)
        {
            var seq = sequencer.Record(n, motor);
            var state = StateFor(seq % 1000);
            buffer.Record(seq, motor, state);
            sequencer.TryTakeBatch(out _, out _);
            if (seq > 3)
            {
                buffer.Acknowledge(seq - 3, StateFor((seq - 3) % 1000));
            }
        }

        for (uint n = 1; n <= 500; n++)
        {
            Tick(n);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (uint n = 501; n <= 10_500; n++)
        {
            Tick(n);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
