using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Testing.Generators;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// The frames that stay readable in every protocol version: the start of ClientHello and the whole HandshakeReject, so that a client of any
/// version can be told to update.
/// </summary>
public sealed class FrozenFrameTests
{
    [Theory]
    [InlineData("01 01 01 00", 1)]
    [InlineData("01 02 05 00", 5)]
    [InlineData("01 07 02 01", 0x0102)]
    [InlineData("01 FF FF FF", 0xFFFF)]
    [InlineData("01 00 00 00", 0)]
    [InlineData("01 80 34 12 AA BB CC DD", 0x1234)]
    public void TryReadProtocolVersion_ClientHelloWithAnyFormatVersion_ReadsBytesTwoAndThree(string hex, int expected)
    {
        var ok = MessageCodec.TryReadProtocolVersion(Kit.Hex(hex), out var version);

        Assert.True(ok);
        Assert.Equal((ushort)expected, version);
    }

    [Fact]
    public void TryReadProtocolVersion_ValidClientHello_ReturnsItsProtocolVersion()
    {
        var hello = new ClientHello(ProtocolConstants.ProtocolVersion, 1, new Core.Ids.PlatformIdentity(Core.Ids.IdentityPlatform.Dev, 1), "a", "b", []);

        Assert.True(MessageCodec.TryReadProtocolVersion(Kit.Encode(hello), out var version));
        Assert.Equal(ProtocolConstants.ProtocolVersion, version);
    }

    [Fact]
    public void ClientHello_WithFutureFormatVersionAndAnyBody_StillYieldsItsProtocolVersion()
    {
        // A future client: FormatVersion 9, ProtocolVersion 77, then a body this build cannot parse.
        var frame = Kit.Hex("01 09 4D 00 DE AD BE EF 01 02 03");

        Assert.True(MessageCodec.TryReadProtocolVersion(frame, out var version));
        Assert.Equal((ushort)77, version);
        Kit.AssertViolation(d => MessageCodec.Read(d), frame);
    }

    [Fact]
    public void TryReadProtocolVersion_AnyGeneratedClientHelloWithAnyFormatVersion_ReadsTheVersion() =>
        Kit.ForAll(
            CodecGen.ClientHello.SelectMany(h => Gen.Choose(0, 255).Select(fv => (Hello: h, FormatVersion: (byte)fv))),
            pair =>
            {
                var frame = Kit.Encode(pair.Hello);
                frame[1] = pair.FormatVersion;

                Assert.True(MessageCodec.TryReadProtocolVersion(frame, out var version));
                Assert.Equal(pair.Hello.ProtocolVersion, version);
            });

    [Theory]
    [InlineData("")]
    [InlineData("01")]
    [InlineData("01 01")]
    [InlineData("01 01 05")]
    [InlineData("02 01 01 00")]
    [InlineData("03 01 01 00")]
    [InlineData("00 01 01 00")]
    [InlineData("12 01 40 42 0F 00 00 00 00 00")]
    [InlineData("FF FF FF FF")]
    public void TryReadProtocolVersion_NotAClientHelloOrTooShort_ReturnsFalse(string hex)
    {
        var ok = MessageCodec.TryReadProtocolVersion(Kit.Hex(hex), out var version);

        Assert.False(ok);
        Assert.Equal((ushort)0, version);
    }

    [Property(MaxTest = Kit.Cases)]
    public void TryReadProtocolVersion_ArbitraryBytes_NeverThrows(byte[] frame)
    {
        frame ??= [];

        var ok = MessageCodec.TryReadProtocolVersion(frame, out var version);

        Assert.Equal(ok, frame.Length >= 4 && frame[0] == 1);
        if (ok)
        {
            Assert.Equal((ushort)(frame[2] | (frame[3] << 8)), version);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(255)]
    public void HandshakeReject_WithUnknownCode_DecodesAndKeepsTheCode(int code)
    {
        var reject = Assert.IsType<HandshakeReject>(MessageCodec.Read(Kit.Hex($"03 01 {code:X2} 03 61 62 63")));

        Assert.Equal((byte)code, (byte)reject.Code);
        Assert.Equal("abc", reject.Detail);
    }

    [Fact]
    public void HandshakeReject_UnknownCode_ReencodesToTheSameBytes()
    {
        var frame = Kit.Hex("03 01 C8 03 61 62 63");

        var reject = Assert.IsType<HandshakeReject>(MessageCodec.Read(frame));

        Assert.Equal(frame, Kit.Encode(reject));
    }

    [Theory]
    [InlineData(HandshakeRejectCode.WrongProtocol)]
    [InlineData(HandshakeRejectCode.WrongBuild)]
    [InlineData(HandshakeRejectCode.ServerFull)]
    [InlineData(HandshakeRejectCode.BadPassword)]
    [InlineData(HandshakeRejectCode.Banned)]
    [InlineData(HandshakeRejectCode.AlreadyConnected)]
    [InlineData(HandshakeRejectCode.IdentityMismatch)]
    public void HandshakeReject_IsAlwaysTypeThreeVersionOneCodeDetail(HandshakeRejectCode code)
    {
        var frame = Kit.Encode(new HandshakeReject(code, "d"));

        Assert.Equal(new byte[] { 0x03, 0x01, (byte)code, 0x01, (byte)'d' }, frame);
    }

    [Fact]
    public void HandshakeReject_WithLongestDetail_HasTheExpectedLength()
    {
        var frame = Kit.Encode(new HandshakeReject(HandshakeRejectCode.WrongProtocol, new string('x', ProtocolConstants.MaxRejectDetailBytes)));

        Assert.Equal(3 + 2 + ProtocolConstants.MaxRejectDetailBytes, frame.Length);
    }
}
