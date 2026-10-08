using System.Numerics;

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

using Horyzont.Core.Serialization;
using Horyzont.Testing.Generators;

namespace Horyzont.Core.Tests.Serialization;

public sealed class ByteReaderTests
{
    private static void Bad<T>(string hex, ReadFn<T> read) => Kit.AssertViolation(d => Kit.ReadAll(d, read)!, Kit.Hex(hex));

    private static T Good<T>(string hex, ReadFn<T> read) => Kit.ReadAll(Kit.Hex(hex), read);

    [Fact]
    public void PositionAndRemaining_TrackConsumedBytes()
    {
        var r = new ByteReader(Kit.Hex("01 02 03 04 05"));

        Assert.Equal(0, r.Position);
        Assert.Equal(5, r.Remaining);
        r.ReadUInt16();
        Assert.Equal(2, r.Position);
        Assert.Equal(3, r.Remaining);
    }

    [Fact]
    public void EnsureEnd_WithUnreadBytes_ThrowsProtocolViolation()
    {
        Assert.Throws<ProtocolViolationException>(() =>
        {
            var r = new ByteReader(Kit.Hex("01 02"));
            r.ReadByte();
            r.EnsureEnd();
        });
    }

    [Fact]
    public void EnsureEnd_AtEnd_DoesNotThrow()
    {
        var r = new ByteReader(Kit.Hex("01"));
        r.ReadByte();

        r.EnsureEnd();
    }

    [Fact]
    public void ReadFixedWidthIntegers_AreLittleEndian()
    {
        Assert.Equal((ushort)0x0102, Good("02 01", (ref ByteReader r) => r.ReadUInt16()));
        Assert.Equal(0x01020304u, Good("04 03 02 01", (ref ByteReader r) => r.ReadUInt32()));
        Assert.Equal(0x0102030405060708UL, Good("08 07 06 05 04 03 02 01", (ref ByteReader r) => r.ReadUInt64()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("01")]
    public void ReadUInt16_Truncated_ThrowsProtocolViolation(string hex) => Bad(hex, (ref ByteReader r) => r.ReadUInt16());

    [Theory]
    [InlineData("")]
    [InlineData("01 02 03")]
    public void ReadUInt32_Truncated_ThrowsProtocolViolation(string hex) => Bad(hex, (ref ByteReader r) => r.ReadUInt32());

    [Theory]
    [InlineData("")]
    [InlineData("01 02 03 04 05 06 07")]
    public void ReadUInt64_Truncated_ThrowsProtocolViolation(string hex) => Bad(hex, (ref ByteReader r) => r.ReadUInt64());

    [Fact]
    public void ReadByte_Empty_ThrowsProtocolViolation() => Bad("", (ref ByteReader r) => r.ReadByte());

    [Fact]
    public void ReadBool_ZeroAndOne_AreAccepted()
    {
        Assert.False(Good("00", (ref ByteReader r) => r.ReadBool()));
        Assert.True(Good("01", (ref ByteReader r) => r.ReadBool()));
    }

    [Theory]
    [InlineData("02")]
    [InlineData("FF")]
    public void ReadBool_OtherValue_ThrowsProtocolViolation(string hex) => Bad(hex, (ref ByteReader r) => r.ReadBool());

    [Fact]
    public void ReadSByte_MinusOneTwentySeven_IsAccepted()
    {
        Assert.Equal((sbyte)-127, Good("81", (ref ByteReader r) => r.ReadSByte()));
        Assert.Equal((sbyte)127, Good("7F", (ref ByteReader r) => r.ReadSByte()));
        Assert.Equal((sbyte)-1, Good("FF", (ref ByteReader r) => r.ReadSByte()));
    }

    [Fact]
    public void ReadSByte_MinusOneTwentyEight_ThrowsProtocolViolation() => Bad("80", (ref ByteReader r) => r.ReadSByte());

    [Theory]
    [InlineData("00", 0UL)]
    [InlineData("7F", 127UL)]
    [InlineData("80 01", 128UL)]
    [InlineData("AC 02", 300UL)]
    [InlineData("FF FF FF FF 0F", 4294967295UL)]
    [InlineData("FF FF FF FF FF FF FF FF FF 01", ulong.MaxValue)]
    public void ReadVarUInt_CanonicalEncodings_AreDecoded(string hex, ulong expected) =>
        Assert.Equal(expected, Good(hex, (ref ByteReader r) => r.ReadVarUInt()));

    [Theory]
    [InlineData("80 00")]
    [InlineData("FF 00")]
    [InlineData("80 80 00")]
    [InlineData("AC 82 00")]
    public void ReadVarUInt_RedundantEncoding_ThrowsProtocolViolation(string hex) => Bad(hex, (ref ByteReader r) => r.ReadVarUInt());

    [Theory]
    [InlineData("FF FF FF FF FF FF FF FF FF 02")]
    [InlineData("FF FF FF FF FF FF FF FF FF 7F")]
    [InlineData("80 80 80 80 80 80 80 80 80 80 01")]
    [InlineData("FF FF FF FF FF FF FF FF FF FF FF 01")]
    public void ReadVarUInt_OverflowPast64Bits_ThrowsProtocolViolation(string hex) => Bad(hex, (ref ByteReader r) => r.ReadVarUInt());

    [Theory]
    [InlineData("")]
    [InlineData("80")]
    [InlineData("FF FF")]
    [InlineData("FF FF FF FF FF FF FF FF FF")]
    public void ReadVarUInt_TruncatedContinuation_ThrowsProtocolViolation(string hex) => Bad(hex, (ref ByteReader r) => r.ReadVarUInt());

    [Fact]
    public void ReadVarUInt32_WithinRange_IsDecoded()
    {
        Assert.Equal(uint.MaxValue, Good("FF FF FF FF 0F", (ref ByteReader r) => r.ReadVarUInt32()));
        Assert.Equal(0u, Good("00", (ref ByteReader r) => r.ReadVarUInt32()));
    }

    [Theory]
    [InlineData("80 80 80 80 10")]
    [InlineData("FF FF FF FF 1F")]
    [InlineData("80 80 80 80 80 80 80 80 80 01")]
    public void ReadVarUInt32_OutOfRange_ThrowsProtocolViolation(string hex) => Bad(hex, (ref ByteReader r) => r.ReadVarUInt32());

    [Fact]
    public void ReadVarUInt16_WithinRange_IsDecoded()
    {
        Assert.Equal(ushort.MaxValue, Good("FF FF 03", (ref ByteReader r) => r.ReadVarUInt16()));
        Assert.Equal((ushort)300, Good("AC 02", (ref ByteReader r) => r.ReadVarUInt16()));
    }

    [Theory]
    [InlineData("80 80 04")]
    [InlineData("FF FF 07")]
    [InlineData("80 80 80 80 10")]
    public void ReadVarUInt16_OutOfRange_ThrowsProtocolViolation(string hex) => Bad(hex, (ref ByteReader r) => r.ReadVarUInt16());

    [Theory]
    [InlineData("00", 0L)]
    [InlineData("01", -1L)]
    [InlineData("02", 1L)]
    [InlineData("03", -2L)]
    [InlineData("FE FF FF FF 0F", 2147483647L)]
    [InlineData("FF FF FF FF 0F", -2147483648L)]
    [InlineData("FE FF FF FF FF FF FF FF FF 01", long.MaxValue)]
    [InlineData("FF FF FF FF FF FF FF FF FF 01", long.MinValue)]
    public void ReadVarInt_ZigzagEncodings_AreDecoded(string hex, long expected) =>
        Assert.Equal(expected, Good(hex, (ref ByteReader r) => r.ReadVarInt()));

    [Theory]
    [InlineData("80 80 80 80 10")]
    [InlineData("81 80 80 80 10")]
    [InlineData("FF FF FF FF 1F")]
    public void ReadVarInt32_OutOfRange_ThrowsProtocolViolation(string hex) => Bad(hex, (ref ByteReader r) => r.ReadVarInt32());

    [Fact]
    public void ReadVarInt32_IntLimits_AreDecoded()
    {
        Assert.Equal(int.MaxValue, Good("FE FF FF FF 0F", (ref ByteReader r) => r.ReadVarInt32()));
        Assert.Equal(int.MinValue, Good("FF FF FF FF 0F", (ref ByteReader r) => r.ReadVarInt32()));
        Assert.Equal(-1, Good("01", (ref ByteReader r) => r.ReadVarInt32()));
    }

    [Theory]
    [InlineData("00 00 C0 7F")]
    [InlineData("01 00 C0 7F")]
    [InlineData("00 00 80 7F")]
    [InlineData("00 00 80 FF")]
    [InlineData("FF FF FF FF")]
    public void ReadSingle_NaNOrInfinity_ThrowsProtocolViolation(string hex) => Bad(hex, (ref ByteReader r) => r.ReadSingle());

    [Fact]
    public void ReadSingle_FiniteValues_AreDecoded()
    {
        Assert.Equal(1.0f, Good("00 00 80 3F", (ref ByteReader r) => r.ReadSingle()));
        Assert.Equal(float.MaxValue, Good("FF FF 7F 7F", (ref ByteReader r) => r.ReadSingle()));
        Assert.Equal(float.Epsilon, Good("01 00 00 00", (ref ByteReader r) => r.ReadSingle()));
    }

    [Theory]
    [InlineData("00 7E")]
    [InlineData("01 7E")]
    [InlineData("00 7C")]
    [InlineData("00 FC")]
    [InlineData("FF FF")]
    public void ReadHalf_NaNOrInfinity_ThrowsProtocolViolation(string hex) => Bad(hex, (ref ByteReader r) => r.ReadHalf());

    [Fact]
    public void ReadHalf_FiniteValues_AreWidenedToFloat()
    {
        Assert.Equal(1.0f, Good("00 3C", (ref ByteReader r) => r.ReadHalf()));
        Assert.Equal(-2.0f, Good("00 C0", (ref ByteReader r) => r.ReadHalf()));
        Assert.Equal(65504f, Good("FF 7B", (ref ByteReader r) => r.ReadHalf()));
    }

    [Fact]
    public void ReadVector3_ReadsXyz() =>
        Assert.Equal(new Vector3(1, 2, 3), Good("00 00 80 3F  00 00 00 40  00 00 40 40", (ref ByteReader r) => r.ReadVector3()));

    [Fact]
    public void ReadVector3_NonFiniteComponent_ThrowsProtocolViolation() =>
        Bad("00 00 80 3F  00 00 C0 7F  00 00 40 40", (ref ByteReader r) => r.ReadVector3());

    [Fact]
    public void ReadVector3_Truncated_ThrowsProtocolViolation() => Bad("00 00 80 3F  00 00 00 40  00 00 40", (ref ByteReader r) => r.ReadVector3());

    [Fact]
    public void ReadQuaternion_Identity_IsDecoded() =>
        Assert.Equal(Quaternion.Identity, Good("00 00 00 00  00 00 00 00  00 00 00 00  00 00 80 3F", (ref ByteReader r) => r.ReadQuaternion()));

    [Theory]
    [InlineData("00 00 00 40  00 00 00 00  00 00 00 00  00 00 00 00")]
    [InlineData("00 00 00 00  00 00 00 00  00 00 00 00  00 00 00 00")]
    [InlineData("00 00 00 00  00 00 00 00  00 00 00 00  00 00 80 3E")]
    [InlineData("00 00 C0 7F  00 00 00 00  00 00 00 00  00 00 80 3F")]
    [InlineData("00 00 00 00  00 00 00 00  00 00 00 00  00 00 80 7F")]
    public void ReadQuaternion_NotUnitLengthOrNonFinite_ThrowsProtocolViolation(string hex) =>
        Bad(hex, (ref ByteReader r) => r.ReadQuaternion());

    [Fact]
    public void ReadQuaternion_Truncated_ThrowsProtocolViolation() =>
        Bad("00 00 00 00  00 00 00 00  00 00 00 00  00 00 80", (ref ByteReader r) => r.ReadQuaternion());

    [Fact]
    public void ReadGuid_UsesDefaultGuidLayout() =>
        Assert.Equal(
            new Guid("00112233-4455-6677-8899-aabbccddeeff"),
            Good("33 22 11 00  55 44  77 66  88 99 AA BB CC DD EE FF", (ref ByteReader r) => r.ReadGuid()));

    [Fact]
    public void ReadGuid_Truncated_ThrowsProtocolViolation() => Bad("33 22 11 00  55 44  77 66  88 99 AA BB CC DD EE", (ref ByteReader r) => r.ReadGuid());

    [Theory]
    [InlineData("00", "")]
    [InlineData("03 41 6E 6E", "Ann")]
    [InlineData("02 C4 85", "ą")]
    [InlineData("04 F0 9F 98 80", "\U0001F600")]
    public void ReadString_ValidUtf8_IsDecoded(string hex, string expected) =>
        Assert.Equal(expected, Good(hex, (ref ByteReader r) => r.ReadString(32)));

    [Theory]
    [InlineData("01 FF")]
    [InlineData("01 80")]
    [InlineData("02 C0 80")]
    [InlineData("03 ED A0 80")]
    [InlineData("01 C3")]
    [InlineData("04 F4 90 80 80")]
    [InlineData("03 E0 80 80")]
    public void ReadString_InvalidUtf8_ThrowsProtocolViolation(string hex) => Bad(hex, (ref ByteReader r) => r.ReadString(32));

    [Fact]
    public void ReadString_ExactlyAtLimit_IsAccepted() =>
        Assert.Equal(new string('x', 4), Good("04 78 78 78 78", (ref ByteReader r) => r.ReadString(4)));

    [Fact]
    public void ReadString_OneByteOverLimit_ThrowsProtocolViolation() => Bad("05 78 78 78 78 78", (ref ByteReader r) => r.ReadString(4));

    [Theory]
    [InlineData("05 78")]
    [InlineData("FF FF FF FF 07")]
    [InlineData("80 80 80 80 80 80 80 80 80 01")]
    public void ReadString_LengthBeyondDataOrLimit_ThrowsProtocolViolation(string hex) => Bad(hex, (ref ByteReader r) => r.ReadString(1024));

    [Fact]
    public void ReadBytes_ReturnsNewArray()
    {
        var source = Kit.Hex("03 01 02 03");

        var result = Kit.ReadAll(source, (ref ByteReader r) => r.ReadBytes(8));

        Assert.Equal(new byte[] { 1, 2, 3 }, result);
        result[0] = 99;
        Assert.Equal(1, source[1]);
    }

    [Fact]
    public void ReadBytes_Empty_ReturnsEmptyArray() => Assert.Empty(Good("00", (ref ByteReader r) => r.ReadBytes(8)));

    [Theory]
    [InlineData("04 01 02 03 04")]
    [InlineData("05 01 02")]
    [InlineData("FF FF FF FF 07")]
    public void ReadBytes_OverLimitOrBeyondData_ThrowsProtocolViolation(string hex) => Bad(hex, (ref ByteReader r) => r.ReadBytes(3));

    [Fact]
    public void ReadCount_AtLimit_IsAccepted()
    {
        Assert.Equal(0, Good("00", (ref ByteReader r) => r.ReadCount(5)));
        Assert.Equal(5, Good("05", (ref ByteReader r) => r.ReadCount(5)));
        Assert.Equal(2048, Good("80 10", (ref ByteReader r) => r.ReadCount(2048)));
    }

    [Theory]
    [InlineData("06")]
    [InlineData("FF FF FF FF 07")]
    [InlineData("FF FF FF FF 0F")]
    [InlineData("FF FF FF FF FF FF FF FF FF 01")]
    [InlineData("80 00")]
    public void ReadCount_OverLimitOrMalformed_ThrowsProtocolViolation(string hex) => Bad(hex, (ref ByteReader r) => r.ReadCount(5));

    [Fact]
    public void Read_SequenceOfFields_ConsumesInOrder()
    {
        var bytes = Kit.Write(w =>
        {
            w.WriteVarUInt(300);
            w.WriteString("hi", 8);
            w.WriteBool(true);
            w.WriteVarInt(-5);
        });

        var r = new ByteReader(bytes);
        Assert.Equal(300UL, r.ReadVarUInt());
        Assert.Equal("hi", r.ReadString(8));
        Assert.True(r.ReadBool());
        Assert.Equal(-5L, r.ReadVarInt());
        r.EnsureEnd();
    }

    [Property(MaxTest = Kit.Cases)]
    public void VarUInt_RoundTrips(ulong value)
    {
        var bytes = Kit.Write(w => w.WriteVarUInt(value));

        Assert.Equal(value, Kit.ReadAll(bytes, (ref ByteReader r) => r.ReadVarUInt()));
        Assert.InRange(bytes.Length, 1, 10);
    }

    [Property(MaxTest = Kit.Cases)]
    public void VarInt_RoundTrips(long value)
    {
        var bytes = Kit.Write(w => w.WriteVarInt(value));

        Assert.Equal(value, Kit.ReadAll(bytes, (ref ByteReader r) => r.ReadVarInt()));
    }

    [Property(MaxTest = Kit.Cases)]
    public void VarUInt32And16_RoundTripWithinRange(uint value)
    {
        var bytes = Kit.Write(w => w.WriteVarUInt(value));

        Assert.Equal(value, Kit.ReadAll(bytes, (ref ByteReader r) => r.ReadVarUInt32()));
        if (value <= ushort.MaxValue)
        {
            Assert.Equal((ushort)value, Kit.ReadAll(bytes, (ref ByteReader r) => r.ReadVarUInt16()));
        }
        else
        {
            Assert.Throws<ProtocolViolationException>(() => Kit.ReadAll(bytes, (ref ByteReader r) => r.ReadVarUInt16()));
        }
    }

    [Property(MaxTest = Kit.Cases)]
    public void VarInt32_RoundTrips(int value)
    {
        var bytes = Kit.Write(w => w.WriteVarInt(value));

        Assert.Equal(value, Kit.ReadAll(bytes, (ref ByteReader r) => r.ReadVarInt32()));
    }

    [Property(MaxTest = Kit.Cases)]
    public void VarInt32_ValueBeyondIntRange_ThrowsProtocolViolation(int value, bool above)
    {
        var big = above ? (long)int.MaxValue + 1 + Math.Abs((long)value) : (long)int.MinValue - 1 - Math.Abs((long)value);
        var bytes = Kit.Write(w => w.WriteVarInt(big));

        Assert.Throws<ProtocolViolationException>(() => Kit.ReadAll(bytes, (ref ByteReader r) => r.ReadVarInt32()));
    }

    [Property(MaxTest = Kit.Cases)]
    public void Fixed64And32And16_RoundTrip(ulong a, uint b, ushort c, byte d, sbyte e, bool f)
    {
        var bytes = Kit.Write(w =>
        {
            w.WriteUInt64(a);
            w.WriteUInt32(b);
            w.WriteUInt16(c);
            w.WriteByte(d);
            w.WriteSByte(e);
            w.WriteBool(f);
        });

        if (e == sbyte.MinValue)
        {
            Assert.Throws<ProtocolViolationException>(() => ReadFixed(bytes));
            return;
        }

        Assert.Equal((a, b, c, d, e, f), ReadFixed(bytes));
    }

    [Property(MaxTest = Kit.Cases, Arbitrary = [typeof(FiniteFloatArbitrary)])]
    public void Single_RoundTripsBitExact(float value)
    {
        var bytes = Kit.Write(w => w.WriteSingle(value));

        var back = Kit.ReadAll(bytes, (ref ByteReader r) => r.ReadSingle());
        Assert.Equal(BitConverter.SingleToInt32Bits(value), BitConverter.SingleToInt32Bits(back));
    }

    [Property(MaxTest = Kit.Cases, Arbitrary = [typeof(HalfFloatArbitrary)])]
    public void Half_RoundTripsToNearestHalf(float value)
    {
        var bytes = Kit.Write(w => w.WriteHalf(value));

        Assert.Equal(2, bytes.Length);
        Assert.Equal(Canon.ViaHalf(value), Kit.ReadAll(bytes, (ref ByteReader r) => r.ReadHalf()));
    }

    [Property(MaxTest = Kit.Cases, Arbitrary = [typeof(StringArbitrary)])]
    public void String_RoundTripsAndPrefixIsUtf8ByteLength(string value)
    {
        var bytes = Kit.Write(w => w.WriteString(value, 64));

        Assert.Equal(value, Kit.ReadAll(bytes, (ref ByteReader r) => r.ReadString(64)));
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(value), bytes[0]);
    }

    [Property(MaxTest = Kit.Cases)]
    public void Guid_RoundTrips(Guid value)
    {
        var bytes = Kit.Write(w => w.WriteGuid(value));

        Assert.Equal(16, bytes.Length);
        Assert.Equal(value, Kit.ReadAll(bytes, (ref ByteReader r) => r.ReadGuid()));
    }

    [Property(MaxTest = Kit.Cases)]
    public void Bytes_RoundTrip(byte[] value)
    {
        value ??= [];
        var bytes = Kit.Write(w => w.WriteBytes(value, 100_000));

        Assert.Equal(value, Kit.ReadAll(bytes, (ref ByteReader r) => r.ReadBytes(100_000)));
    }

    private static (ulong, uint, ushort, byte, sbyte, bool) ReadFixed(byte[] bytes) =>
        Kit.ReadAll(bytes, (ref ByteReader r) => (r.ReadUInt64(), r.ReadUInt32(), r.ReadUInt16(), r.ReadByte(), r.ReadSByte(), r.ReadBool()));

    public static class FiniteFloatArbitrary
    {
        public static Arbitrary<float> Floats() => Arb.From(CodecGen.Finite);
    }

    public static class HalfFloatArbitrary
    {
        public static Arbitrary<float> Floats() => Arb.From(CodecGen.HalfRange);
    }

    public static class StringArbitrary
    {
        public static Arbitrary<string> Strings() => Arb.From(CodecGen.Text(0, 64));
    }
}
