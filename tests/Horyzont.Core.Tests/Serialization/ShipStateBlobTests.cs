using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Ships;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>Blob 12 (K5-24 decision 16): [kind=12][format=1][construct u64][fuel f32][lin 3×f32][ang 3×f32][flags u8], 39 bytes.</summary>
public sealed class ShipStateBlobTests
{
    // Construct 5, Fuel 500 = 43FA0000, Lin (1, -2, 0.5), Ang (0, 0.25, 0), Flags Parked | GearDown = 03.
    private const string Golden =
        "0C 01 05 00 00 00 00 00 00 00 00 00 FA 43 00 00 80 3F 00 00 00 C0 00 00 00 3F 00 00 00 00 00 00 80 3E 00 00 00 00 03";

    private static readonly ShipStateRecord _sample = new(
        new EntityId(5), 500f, new Vector3(1f, -2f, 0.5f), new Vector3(0f, 0.25f, 0f), ShipStateFlags.Parked | ShipStateFlags.GearDown);

    [Fact]
    public void ShipStateBlob_GoldenBytesAndRejectsDamaged()
    {
        var blob = StateCodec.EncodeShipStateBlob(_sample);

        Assert.Equal(Golden, Kit.ToHex(blob));
        Assert.Equal(39, blob.Length);
        Assert.Equal(_sample, StateCodec.DecodeShipStateBlob(blob));

        // Fuel NaN, fuel negative, linear +inf, angular NaN, unknown flag bit, wrong kind, wrong version, truncated, trailing byte.
        AssertRejected(blob, b => BitConverter.TryWriteBytes(b.AsSpan(10), float.NaN));
        AssertRejected(blob, b => BitConverter.TryWriteBytes(b.AsSpan(10), -1f));
        AssertRejected(blob, b => BitConverter.TryWriteBytes(b.AsSpan(14), float.PositiveInfinity));
        AssertRejected(blob, b => BitConverter.TryWriteBytes(b.AsSpan(30), float.NaN));
        AssertRejected(blob, b => b[38] = 0x08);
        AssertRejected(blob, b => b[0] = 3);
        AssertRejected(blob, b => b[1] = 2);
        Assert.Throws<ProtocolViolationException>(() => StateCodec.DecodeShipStateBlob(blob.AsSpan(0, 38)));
        Assert.Throws<ProtocolViolationException>(() => StateCodec.DecodeShipStateBlob([.. blob, 0]));
    }

    [Fact]
    public void ShipStateBlob_InvalidRecord_EncodeThrows() =>
        Assert.Throws<ArgumentException>(() => StateCodec.EncodeShipStateBlob(_sample with { Fuel = -1f }));

    private static void AssertRejected(byte[] blob, Action<byte[]> damage)
    {
        var copy = (byte[])blob.Clone();
        damage(copy);
        Assert.Throws<ProtocolViolationException>(() => StateCodec.DecodeShipStateBlob(copy));
    }
}
