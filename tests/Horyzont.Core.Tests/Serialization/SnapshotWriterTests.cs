using Horyzont.Core.Serialization;
using Horyzont.Testing.Generators;

namespace Horyzont.Core.Tests.Serialization;

public sealed class SnapshotWriterTests
{
    [Fact]
    public void SnapshotWriter_Write_MatchesMessageCodecEncode()
    {
        Kit.ForAll(CodecGen.Snapshot, snapshot =>
        {
            var expected = new ByteWriter();
            MessageCodec.Write(expected, snapshot);
            var output = new byte[1150];
            var size = SnapshotWriter.Write(output, snapshot.SnapshotId, snapshot.ServerTick, snapshot.BaselineSnapshotId,
                snapshot.LastProcessedInputSeq, snapshot.Frames.ToArray(), snapshot.Entities.ToArray());
            Assert.Equal(expected.WrittenSpan.ToArray(), output.AsSpan(0, size).ToArray());
        });
    }
}
