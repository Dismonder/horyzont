using BenchmarkDotNet.Attributes;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;

namespace Horyzont.Benchmarks;

/// <summary>Encoding and decoding of a small reliable reply; encoding into a reused writer allocates nothing.</summary>
[MemoryDiagnoser]
public class CodecBench
{
    private readonly ByteWriter _writer = new(256);
    private readonly IntentReply _reply = new(42, RejectReason.None, new OpId(1234));
    private byte[] _frame = [];

    [GlobalSetup]
    public void Setup()
    {
        _writer.Reset();
        MessageCodec.Write(_writer, _reply);
        _frame = _writer.WrittenSpan.ToArray();
    }

    [Benchmark]
    public int EncodeIntentReply()
    {
        _writer.Reset();
        MessageCodec.Write(_writer, _reply);
        return _writer.Length;
    }

    [Benchmark]
    public Message DecodeIntentReply() => MessageCodec.Read(_frame);
}
