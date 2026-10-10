using Horyzont.Client.Movement;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;

namespace Horyzont.Client.Tests.Movement;

public sealed class InputSequencerTests
{
    private static readonly uint[] _batchSeqs = [2, 4, 6, 8, 10, 12, 14, 16, 18, 20];
    private static readonly int[] _batchCounts = [2, 4, 5, 5, 5, 5, 5, 5, 5, 5];

    /// <summary>Client tick = 10 * seq, so a sent input can be matched with its number.</summary>
    private static uint Tick(uint seq) => seq * 10;

    private static MotorInput Input(float moveX = 0f) => new(moveX, 0f, 0f, 0f, InputButtons.None);

    private static List<(uint FirstSeq, uint[] Ticks)> Run(InputSequencer sequencer, int ticks, Func<uint, bool>? lose = null)
    {
        var sent = new List<(uint, uint[])>();
        for (var i = 0; i < ticks; i++)
        {
            var seq = sequencer.Record(Tick(sequencer.LastSeq + 1), Input());
            if (sequencer.TryTakeBatch(out var firstSeq, out var inputs) && !(lose?.Invoke(seq) ?? false))
            {
                sent.Add((firstSeq, inputs.Select(x => x.ClientTick).ToArray()));
            }
        }

        return sent;
    }

    [Fact]
    public void InputSequencer_EveryTwoTicks_TwoNewPlusThreeRepeats()
    {
        var sequencer = new InputSequencer();
        var batches = new List<(uint Seq, uint FirstSeq, int Count)>();

        for (var tick = 1; tick <= 20; tick++)
        {
            var seq = sequencer.Record((uint)tick, Input());
            Assert.Equal((uint)tick, seq);
            if (sequencer.TryTakeBatch(out var firstSeq, out var inputs))
            {
                batches.Add((seq, firstSeq, inputs.Count));
            }
        }

        // One batch per two ticks; 2, 4, then 5 inputs from the third batch on.
        Assert.Equal(10, batches.Count);
        Assert.Equal(_batchSeqs, batches.Select(b => b.Seq));
        Assert.Equal(_batchCounts, batches.Select(b => b.Count));
        Assert.All(batches, b => Assert.True(b.Count <= ProtocolConstants.MaxInputsPerBatch));

        // firstSeq is the oldest input in the batch: the last number minus the count plus one.
        Assert.All(batches, b => Assert.Equal(b.Seq - (uint)b.Count + 1, b.FirstSeq));
    }

    [Fact]
    public void InputSequencer_Batch_ContainsConsecutiveNumbersInOrder()
    {
        var sequencer = new InputSequencer();
        foreach (var batch in Run(sequencer, 30))
        {
            for (var i = 0; i < batch.Ticks.Length; i++)
            {
                Assert.Equal(Tick(batch.FirstSeq + (uint)i), batch.Ticks[i]);
            }
        }
    }

    [Fact]
    public void InputSequencer_StoredInput_HasQuantizedAxes()
    {
        var sequencer = new InputSequencer();
        MotorInput.TryFrom(new PlayerInput(0, 0.3f, -0.77f, 1f, 0.2f, InputButtons.Jump), new MovementRules(), out var motor);
        sequencer.Record(1, motor);
        sequencer.Record(2, motor);

        Assert.True(sequencer.TryTakeBatch(out _, out var inputs));

        var expectedX = Quantization.DequantizeAxis(Quantization.QuantizeAxis(0.3f));
        var expectedY = Quantization.DequantizeAxis(Quantization.QuantizeAxis(-0.77f));
        Assert.All(inputs, i =>
        {
            Assert.Equal(expectedX, i.MoveX);
            Assert.Equal(expectedY, i.MoveY);
            Assert.Equal(1f, i.Yaw);
            Assert.Equal(InputButtons.Jump, i.Buttons);
        });
    }

    [Fact]
    public void InputSequencer_LostPacket_RepeatsCoverGap()
    {
        var sequencer = new InputSequencer();

        // The batch carried by input 8 (inputs 4..8) is lost; the batch of input 10 must still contain 7 and 8.
        var sent = Run(sequencer, 12, lose: seq => seq == 8);

        var next = sent.Single(b => b.Ticks[^1] == Tick(10));
        Assert.Contains(Tick(7), next.Ticks);
        Assert.Contains(Tick(8), next.Ticks);
        Assert.DoesNotContain(sent, b => b.Ticks[^1] == Tick(8));

        // Over the whole run every input reaches the server at least once despite the single lost batch.
        var covered = sent.SelectMany(b => b.Ticks).ToHashSet();
        for (uint seq = 1; seq <= 12; seq++)
        {
            Assert.Contains(Tick(seq), covered);
        }
    }

    [Fact]
    public void InputSequencer_FrameHitch_EveryInputIsSentInOrderWithinTheBatchLimit()
    {
        var sequencer = new InputSequencer();
        for (var i = 0; i < 20; i++)
        {
            sequencer.Record(Tick(sequencer.LastSeq + 1), Input());
        }

        // One frame after the hitch: the host drains the backlog.
        var sent = new List<(uint FirstSeq, uint[] Ticks)>();
        while (sequencer.TryTakeBatch(out var firstSeq, out var inputs))
        {
            Assert.InRange(inputs.Count, 1, ProtocolConstants.MaxInputsPerBatch);
            sent.Add((firstSeq, inputs.Select(x => x.ClientTick).ToArray()));
            Assert.True(sent.Count <= 20, "the drain loop must terminate");
        }

        var first = new Dictionary<uint, int>();
        for (var b = 0; b < sent.Count; b++)
        {
            for (var i = 0; i < sent[b].Ticks.Length; i++)
            {
                Assert.Equal(Tick(sent[b].FirstSeq + (uint)i), sent[b].Ticks[i]);
                first.TryAdd(sent[b].FirstSeq + (uint)i, b);
            }
        }

        for (uint seq = 1; seq <= 20; seq++)
        {
            Assert.True(first.ContainsKey(seq), $"input {seq} was never sent");
        }

        // New inputs first appear in batch order.
        Assert.Equal(first.OrderBy(p => p.Key).Select(p => p.Value), first.OrderBy(p => p.Key).Select(p => p.Value).Order());
        Assert.False(sequencer.TryTakeBatch(out _, out _));
    }

    [Fact]
    public void InputSequencer_HugeBacklog_DoesNotFailAndKeepsTheNewestInputs()
    {
        var sequencer = new InputSequencer();
        for (var i = 0; i < 1000; i++)
        {
            sequencer.Record(Tick(sequencer.LastSeq + 1), Input());
        }

        var lastTick = 0u;
        while (sequencer.TryTakeBatch(out var firstSeq, out var inputs))
        {
            Assert.Equal(Tick(firstSeq), inputs[0].ClientTick);
            lastTick = inputs[^1].ClientTick;
        }

        Assert.Equal(Tick(1000), lastTick);
    }

    [Fact]
    public void InputSequencer_Reset_StartsAtOneAgain()
    {
        var sequencer = new InputSequencer();
        Run(sequencer, 7);

        sequencer.Reset();

        Assert.Equal(0u, sequencer.LastSeq);
        Assert.Equal(1u, sequencer.Record(1, Input()));
        Assert.False(sequencer.TryTakeBatch(out _, out _));
        sequencer.Record(2, Input());
        Assert.True(sequencer.TryTakeBatch(out var firstSeq, out var inputs));
        Assert.Equal(1u, firstSeq);
        Assert.Equal(2, inputs.Count);
    }

    [Fact]
    public void InputSequencer_BatchIsAcceptedByTheCodec()
    {
        var sequencer = new InputSequencer();
        MotorInput.TryFrom(new PlayerInput(0, 0.5f, 1f, 2f, 0.1f, InputButtons.Sprint), new MovementRules(), out var motor);
        for (var i = 0; i < 6; i++)
        {
            sequencer.Record((uint)i, motor);
        }

        // Three batches are due for six waiting inputs; the last one is a full batch of five (inputs 2..6).
        var batches = 0;
        while (sequencer.TryTakeBatch(out var firstSeq, out var inputs))
        {
            batches++;
            var writer = new ByteWriter();
            MessageCodec.Write(writer, new InputBatch(firstSeq, inputs));
            var decoded = Assert.IsType<InputBatch>(MessageCodec.Read(writer.WrittenSpan));

            Assert.Equal(firstSeq, decoded.FirstSeq);
            Assert.Equal(inputs.Count, decoded.Inputs.Count);
            Assert.Equal(inputs[0], decoded.Inputs[0]);
            if (batches == 3)
            {
                Assert.Equal(5, decoded.Inputs.Count);
                Assert.Equal(2u, firstSeq);
            }
        }

        Assert.Equal(3, batches);
    }
}
