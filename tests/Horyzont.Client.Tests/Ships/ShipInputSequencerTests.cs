using Horyzont.Client.Ships;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Client.Tests.Ships;

/// <summary>K5-25 decision 11 and Y5: numbering and packing of the pilot inputs.</summary>
public sealed class ShipInputSequencerTests
{
    private static ShipControl Control(uint seq) => new((sbyte)(seq % 100), 127, (sbyte)-(int)(seq % 50), 10, -20, 30, ShipButtons.Assist | ShipButtons.PulseDrive);

    [Fact]
    public void ShipInputSequencer_EveryTick_NewInputAndThreeRepeats()
    {
        var sequencer = new ShipInputSequencer();
        for (uint seq = 1; seq <= 10; seq++)
        {
            Assert.Equal(seq, sequencer.Record(seq + 100, Control(seq)));
            Assert.True(sequencer.TryTakeBatch(out var first, out var inputs));
            Assert.Equal(Math.Min(seq, 4u), (uint)inputs.Count);
            Assert.Equal(seq - (uint)inputs.Count + 1, first);
            Assert.Equal(new ShipInput(seq + 100, Control(seq)), inputs[^1]);
            Assert.False(sequencer.TryTakeBatch(out _, out _));
        }
    }

    [Fact]
    public void ShipInputSequencer_Backlog_TwoNewPerBatchAllSentInOrder()
    {
        var sequencer = new ShipInputSequencer();
        for (uint seq = 1; seq <= 7; seq++)
        {
            sequencer.Record(seq, Control(seq));
        }

        var sent = new List<uint>();
        while (sequencer.TryTakeBatch(out var first, out var inputs))
        {
            Assert.InRange(inputs.Count, 1, 5);
            sent.Add(first + (uint)inputs.Count - 1);
        }

        Assert.Equal([2u, 4u, 6u, 7u], sent);
    }

    [Fact]
    public void ShipInputBatch_SteadyState_Below4KBps()
    {
        // R13: payload bytes of the message (type + body) without the transport header, one batch per tick at 60 Hz.
        var sequencer = new ShipInputSequencer();
        var writer = new ByteWriter();
        long bytes = 0;
        for (uint seq = 1; seq <= 600; seq++)
        {
            sequencer.Record(seq, Control(seq));
            while (sequencer.TryTakeBatch(out var first, out var inputs))
            {
                writer.Reset();
                var batch = new ShipInputBatch(first, inputs.ToArray());
                MessageCodec.Write(writer, batch);
                Assert.Equal(batch, MessageCodec.Read(writer.WrittenSpan), new BatchComparer());
                if (seq > 60)
                {
                    bytes += writer.Length;
                }
            }
        }

        var perSecond = bytes / 9.0;
        Assert.InRange(perSecond, 1, 4096);
        TestContext.Current.SendDiagnosticMessage($"ShipInputBatch payload {perSecond:F0} B/s ({bytes / 540.0:F1} B per batch)");
    }

    [Fact]
    public void ShipInputSequencer_RecordAndTakeAllocateZero()
    {
        var sequencer = new ShipInputSequencer();
        for (uint seq = 1; seq <= 20; seq++)
        {
            sequencer.Record(seq, Control(seq));
            sequencer.TryTakeBatch(out _, out _);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (uint seq = 21; seq <= 1000; seq++)
        {
            sequencer.Record(seq, Control(seq));
            sequencer.TryTakeBatch(out _, out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private sealed class BatchComparer : IEqualityComparer<Message>
    {
        public bool Equals(Message? x, Message? y) =>
            x is ShipInputBatch a && y is ShipInputBatch b && a.FirstSeq == b.FirstSeq && a.Inputs.SequenceEqual(b.Inputs);

        public int GetHashCode(Message obj) => 0;
    }
}
