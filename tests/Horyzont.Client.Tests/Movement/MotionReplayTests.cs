using System.Numerics;

using Horyzont.Client.Movement;
using Horyzont.Core.Motion;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Movement;

public sealed class MotionReplayTests
{
    [Fact]
    public void Replay_UnacknowledgedInputs_MatchesImmediateAuthoritativeReplay()
    {
        var movement = new MovementRules();
        for (var length = 1; length <= 128; length++)
        {
            var buffer = new PredictionBuffer(new CorrectionPolicy { MaxUnacknowledged = 128 }, NullLogger<PredictionBuffer>.Instance);
            var sequencer = new InputSequencer();
            var state = new PredictedState(Vector3.Zero, Vector3.Zero, Quaternion.Identity, 0, MovementMode.Walking, true);
            for (uint seq = 1; seq <= length; seq++)
            {
                var wire = new Horyzont.Core.Protocol.Messages.PlayerInput(seq, 0.37f, 0.72f, 0.1f, 0, Horyzont.Core.Protocol.Messages.InputButtons.None);
                Assert.True(MotorInput.TryFrom(wire, movement, out var input));
                Assert.Equal(seq, sequencer.Record(seq, input));
                state = Integrate(state, input);
                buffer.Record(seq, input, state);
            }
            var server = state with { Feet = Vector3.UnitZ * 0.2f };
            Assert.Equal(AckOutcome.Correct, buffer.Acknowledge(0, server));
            var entries = new PredictionEntry[128];
            var states = new PredictedState[128];
            var count = buffer.CopyUnacknowledged(entries);
            var immediate = server;
            var replay = server;
            for (var i = 0; i < count; i++)
            {
                Assert.Equal((uint)i + 1, entries[i].Seq);
                immediate = Integrate(immediate, entries[i].Input);
                replay = Integrate(replay, entries[i].Input);
                states[i] = replay;
            }
            Assert.Equal(immediate, replay);
            buffer.ReplaceUnacknowledgedStates(states.AsSpan(0, count));
            Assert.Equal(AckOutcome.InSync, buffer.Acknowledge((uint)length, immediate));
        }
    }

    [Fact]
    public void Acknowledge_StaleOrMissingHistory_DoesNotReplayTwice()
    {
        var buffer = new PredictionBuffer(new CorrectionPolicy(), NullLogger<PredictionBuffer>.Instance);
        var state = new PredictedState(Vector3.Zero, Vector3.Zero, Quaternion.Identity, 0, MovementMode.Walking, true);
        var sequencer = new InputSequencer();
        var input = MotorInput.Neutral(0, 0);
        for (uint tick = 1; tick <= 180; tick++)
        {
            var seq = sequencer.Record(tick, input);
            Assert.Equal(tick, seq);
            if (!buffer.IsPaused) { Assert.True(buffer.Record(seq, input, state)); }
        }
        Assert.Equal(120, buffer.Count);
        Assert.Equal(AckOutcome.Correct, buffer.Acknowledge(150, state));
        Assert.Equal(AckOutcome.Stale, buffer.Acknowledge(150, state));
        Assert.Equal(AckOutcome.Stale, buffer.Acknowledge(149, state));
        Assert.Equal(0, buffer.Count);
        buffer.Clear();
        sequencer.Reset();
        Assert.Equal(1u, sequencer.Record(1, input));
        Assert.True(buffer.Record(1, input, state));
        Assert.Equal(AckOutcome.InSync, buffer.Acknowledge(1, state));
    }

    private static PredictedState Integrate(in PredictedState state, in MotorInput input) =>
        state with { Feet = state.Feet + new Vector3(input.MoveX, 0, input.MoveY) / 60f };
}
