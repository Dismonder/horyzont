namespace Horyzont.Core.Tests.Serialization;

/// <summary>The same properties as <see cref="CodecPropertyTests"/> with 200 000 cases, for the nightly run.</summary>
[Trait("Category", "Soak")]
public sealed class CodecSoakTests
{
    [Theory(Timeout = 3_600_000)]
    [MemberData(nameof(CodecCases.Names), MemberType = typeof(CodecCases))]
    public void RoundTrip_ValidValues_DecodeToEqualValue(string codec)
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        CodecProperties.RoundTrip(CodecCases.Get(codec), Kit.SoakCases);
    }

    [Theory(Timeout = 3_600_000)]
    [MemberData(nameof(CodecCases.Names), MemberType = typeof(CodecCases))]
    public void ByteStability_ReencodingDecodedValue_GivesIdenticalBytes(string codec)
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        CodecProperties.ByteStability(CodecCases.Get(codec), Kit.SoakCases);
    }

    [Theory(Timeout = 3_600_000)]
    [MemberData(nameof(CodecCases.Names), MemberType = typeof(CodecCases))]
    public void Truncation_EveryProperPrefix_ThrowsProtocolViolation(string codec)
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        CodecProperties.Truncation(CodecCases.Get(codec), Kit.SoakCases);
    }

    [Theory(Timeout = 3_600_000)]
    [MemberData(nameof(CodecCases.Names), MemberType = typeof(CodecCases))]
    public void Fuzz_RandomBytes_DecodeOrThrowOnlyProtocolViolation(string codec)
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        CodecProperties.FuzzRandomBytes(CodecCases.Get(codec), Kit.SoakCases);
    }

    [Theory(Timeout = 3_600_000)]
    [MemberData(nameof(CodecCases.Names), MemberType = typeof(CodecCases))]
    public void Fuzz_MutatedValidEncodings_DecodeOrThrowOnlyProtocolViolation(string codec)
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        CodecProperties.FuzzMutatedEncodings(CodecCases.Get(codec), Kit.SoakCases);
    }
}
