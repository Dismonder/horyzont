namespace Horyzont.Core.Tests.Serialization;

/// <summary>Table-driven properties over every codec (2 000 cases each).</summary>
public sealed class CodecPropertyTests
{
    [Theory]
    [MemberData(nameof(CodecCases.Names), MemberType = typeof(CodecCases))]
    public void RoundTrip_ValidValues_DecodeToEqualValue(string codec) =>
        CodecProperties.RoundTrip(CodecCases.Get(codec), Kit.Cases);

    [Theory]
    [MemberData(nameof(CodecCases.Names), MemberType = typeof(CodecCases))]
    public void ByteStability_ReencodingDecodedValue_GivesIdenticalBytes(string codec) =>
        CodecProperties.ByteStability(CodecCases.Get(codec), Kit.Cases);

    [Theory]
    [MemberData(nameof(CodecCases.Names), MemberType = typeof(CodecCases))]
    public void Truncation_EveryProperPrefix_ThrowsProtocolViolation(string codec) =>
        CodecProperties.Truncation(CodecCases.Get(codec), Kit.Cases);

    [Theory]
    [MemberData(nameof(CodecCases.NamesRejectingTrailingBytes), MemberType = typeof(CodecCases))]
    public void TrailingBytes_AfterValidEncoding_AreRejected(string codec) =>
        CodecProperties.TrailingBytes(CodecCases.Get(codec), Kit.Cases);

    [Theory(Timeout = 600_000)]
    [MemberData(nameof(CodecCases.Names), MemberType = typeof(CodecCases))]
    public void Fuzz_RandomBytes_DecodeOrThrowOnlyProtocolViolation(string codec)
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        CodecProperties.FuzzRandomBytes(CodecCases.Get(codec), Kit.Cases);
    }

    [Theory(Timeout = 600_000)]
    [MemberData(nameof(CodecCases.Names), MemberType = typeof(CodecCases))]
    public void Fuzz_MutatedValidEncodings_DecodeOrThrowOnlyProtocolViolation(string codec)
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        CodecProperties.FuzzMutatedEncodings(CodecCases.Get(codec), Kit.Cases);
    }
}
