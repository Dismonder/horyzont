using System.Numerics;

using Horyzont.Core.Serialization;

namespace Horyzont.Core.Tests.Serialization;

public sealed class ByteWriterTests
{
    [Fact]
    public void NewWriter_IsEmpty()
    {
        var w = new ByteWriter();

        Assert.Equal(0, w.Length);
        Assert.True(w.WrittenSpan.IsEmpty);
    }

    [Fact]
    public void Write_Bytes_TracksLengthAndContent()
    {
        var w = new ByteWriter();

        w.WriteByte(0x01);
        w.WriteSByte(-1);
        w.WriteBool(true);
        w.WriteBool(false);

        Assert.Equal(4, w.Length);
        Assert.Equal(new byte[] { 0x01, 0xFF, 0x01, 0x00 }, w.WrittenSpan.ToArray());
    }

    [Fact]
    public void Write_FixedWidthIntegers_AreLittleEndian()
    {
        var bytes = Kit.Write(w =>
        {
            w.WriteUInt16(0x0102);
            w.WriteUInt32(0x01020304);
            w.WriteUInt64(0x0102030405060708);
        });

        Assert.Equal(Kit.Hex("02 01  04 03 02 01  08 07 06 05 04 03 02 01"), bytes);
    }

    [Theory]
    [InlineData(0UL, "00")]
    [InlineData(1UL, "01")]
    [InlineData(127UL, "7F")]
    [InlineData(128UL, "80 01")]
    [InlineData(300UL, "AC 02")]
    [InlineData(16383UL, "FF 7F")]
    [InlineData(16384UL, "80 80 01")]
    [InlineData(4294967295UL, "FF FF FF FF 0F")]
    [InlineData(ulong.MaxValue, "FF FF FF FF FF FF FF FF FF 01")]
    public void WriteVarUInt_KnownValues_ProducesLeb128(ulong value, string expected) =>
        Assert.Equal(Kit.Hex(expected), Kit.Write(w => w.WriteVarUInt(value)));

    [Theory]
    [InlineData(0L, "00")]
    [InlineData(-1L, "01")]
    [InlineData(1L, "02")]
    [InlineData(-2L, "03")]
    [InlineData(2L, "04")]
    [InlineData(2147483647L, "FE FF FF FF 0F")]
    [InlineData(-2147483648L, "FF FF FF FF 0F")]
    [InlineData(long.MaxValue, "FE FF FF FF FF FF FF FF FF 01")]
    [InlineData(long.MinValue, "FF FF FF FF FF FF FF FF FF 01")]
    public void WriteVarInt_KnownValues_UsesZigzagThenLeb128(long value, string expected) =>
        Assert.Equal(Kit.Hex(expected), Kit.Write(w => w.WriteVarInt(value)));

    [Theory]
    [InlineData(1.0f, "00 00 80 3F")]
    [InlineData(-2.0f, "00 00 00 C0")]
    [InlineData(0.5f, "00 00 00 3F")]
    [InlineData(0.0f, "00 00 00 00")]
    public void WriteSingle_KnownValues_ProducesIeee754LittleEndian(float value, string expected) =>
        Assert.Equal(Kit.Hex(expected), Kit.Write(w => w.WriteSingle(value)));

    [Theory]
    [InlineData(1.0f, "00 3C")]
    [InlineData(-2.0f, "00 C0")]
    [InlineData(0.0f, "00 00")]
    [InlineData(65504.0f, "FF 7B")]
    [InlineData(-65504.0f, "FF FB")]
    public void WriteHalf_KnownValues_ProducesHalfLittleEndian(float value, string expected) =>
        Assert.Equal(Kit.Hex(expected), Kit.Write(w => w.WriteHalf(value)));

    [Fact]
    public void WriteVector3_WritesXyzAsFloats() =>
        Assert.Equal(Kit.Hex("00 00 80 3F  00 00 00 40  00 00 40 40"), Kit.Write(w => w.WriteVector3(new Vector3(1, 2, 3))));

    [Fact]
    public void WriteQuaternion_WritesXyzwAsFloats() =>
        Assert.Equal(Kit.Hex("00 00 00 00  00 00 00 00  00 00 00 00  00 00 80 3F"), Kit.Write(w => w.WriteQuaternion(Quaternion.Identity)));

    [Fact]
    public void WriteQuaternion_SlightlyOffUnitLength_IsAccepted() =>
        Assert.Equal(16, Kit.Write(w => w.WriteQuaternion(new Quaternion(1.0005f, 0, 0, 0))).Length);

    [Fact]
    public void WriteGuid_UsesDefaultGuidLayout()
    {
        var guid = new Guid("00112233-4455-6677-8899-aabbccddeeff");

        Assert.Equal(Kit.Hex("33 22 11 00  55 44  77 66  88 99 AA BB CC DD EE FF"), Kit.Write(w => w.WriteGuid(guid)));
    }

    [Theory]
    [InlineData("", "00")]
    [InlineData("Ann", "03 41 6E 6E")]
    [InlineData("ą", "02 C4 85")]
    [InlineData("€", "03 E2 82 AC")]
    public void WriteString_PrefixesUtf8ByteLength(string value, string expected) =>
        Assert.Equal(Kit.Hex(expected), Kit.Write(w => w.WriteString(value, 32)));

    [Fact]
    public void WriteString_ExactlyAtLimit_IsAccepted() =>
        Assert.Equal(1 + 32, Kit.Write(w => w.WriteString(new string('x', 32), 32)).Length);

    [Fact]
    public void WriteString_LimitCountsBytesNotCharacters()
    {
        var elevenEuro = new string('€', 11);

        Assert.ThrowsAny<ArgumentException>(() => Kit.Write(w => w.WriteString(elevenEuro, 32)));
    }

    [Fact]
    public void WriteString_OneByteOverLimit_ThrowsArgumentException() =>
        Assert.ThrowsAny<ArgumentException>(() => Kit.Write(w => w.WriteString(new string('x', 33), 32)));

    [Fact]
    public void WriteBytes_PrefixesLength() =>
        Assert.Equal(Kit.Hex("03 01 02 03"), Kit.Write(w => w.WriteBytes(new byte[] { 1, 2, 3 }, 3)));

    [Fact]
    public void WriteBytes_OverLimit_ThrowsArgumentException() =>
        Assert.ThrowsAny<ArgumentException>(() => Kit.Write(w => w.WriteBytes(new byte[] { 1, 2, 3 }, 2)));

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void WriteSingle_NonFinite_ThrowsArgumentException(float value) =>
        Assert.ThrowsAny<ArgumentException>(() => Kit.Write(w => w.WriteSingle(value)));

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(70000f)]
    [InlineData(-70000f)]
    [InlineData(65505f)]
    [InlineData(float.MaxValue)]
    public void WriteHalf_NonFiniteOrOutOfRange_ThrowsArgumentException(float value) =>
        Assert.ThrowsAny<ArgumentException>(() => Kit.Write(w => w.WriteHalf(value)));

    [Theory]
    [InlineData(float.NaN, 0f, 0f)]
    [InlineData(0f, float.PositiveInfinity, 0f)]
    [InlineData(0f, 0f, float.NegativeInfinity)]
    public void WriteVector3_NonFiniteComponent_ThrowsArgumentException(float x, float y, float z) =>
        Assert.ThrowsAny<ArgumentException>(() => Kit.Write(w => w.WriteVector3(new Vector3(x, y, z))));

    [Theory]
    [InlineData(2f, 0f, 0f, 0f)]
    [InlineData(0f, 0f, 0f, 0f)]
    [InlineData(0.5f, 0.5f, 0.5f, 0.6f)]
    [InlineData(1.002f, 0f, 0f, 0f)]
    [InlineData(float.NaN, 0f, 0f, 1f)]
    [InlineData(0f, 0f, 0f, float.PositiveInfinity)]
    public void WriteQuaternion_NotUnitLengthOrNonFinite_ThrowsArgumentException(float x, float y, float z, float w) =>
        Assert.ThrowsAny<ArgumentException>(() => Kit.Write(writer => writer.WriteQuaternion(new Quaternion(x, y, z, w))));

    [Fact]
    public void Reset_ClearsLengthAndNextWriteStartsAtZero()
    {
        var w = new ByteWriter();
        w.WriteUInt32(0xAABBCCDD);

        w.Reset();

        Assert.Equal(0, w.Length);
        Assert.True(w.WrittenSpan.IsEmpty);
        w.WriteByte(7);
        Assert.Equal(new byte[] { 7 }, w.WrittenSpan.ToArray());
    }

    [Fact]
    public void Write_MoreThanInitialCapacity_GrowsAndKeepsContent()
    {
        var w = new ByteWriter(4);

        for (var i = 0; i < 5000; i++)
        {
            w.WriteByte((byte)i);
        }

        Assert.Equal(5000, w.Length);
        var span = w.WrittenSpan;
        for (var i = 0; i < 5000; i++)
        {
            Assert.Equal((byte)i, span[i]);
        }
    }

    [Fact]
    public void Write_AfterResetOfGrownWriter_StillWorks()
    {
        var w = new ByteWriter(1);
        w.WriteBytes(new byte[3000], 3000);
        w.Reset();

        w.WriteVarUInt(300);

        Assert.Equal(Kit.Hex("AC 02"), w.WrittenSpan.ToArray());
    }
}
