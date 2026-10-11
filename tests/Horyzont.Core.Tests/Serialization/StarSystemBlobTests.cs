using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Serialization;
using Horyzont.Core.Space;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>Blob 10 (K5-21 decision 13): [kind][format][system u64][layout u8][home u64][sun f32][count u8]{[planet u64][index u8][pos 3×f64][rot 4×f64]}.</summary>
public sealed class StarSystemBlobTests
{
    private static StarSystemParams Small => new(
        new SystemId(1), 1, new PlanetId(0x0102), 1800f,
        [
            new BodyPlacement(new PlanetId(0x0102), 0, PoseD.Identity),
            new BodyPlacement(new PlanetId(0x0A0B), 1, new PoseD(new Vector3D(18_000, -1, 2.5), QuaternionD.Identity)),
        ]);

    [Fact]
    public void StarSystemBlob_GoldenBytesAndRejectsDamaged()
    {
        var blob = StateCodec.EncodeStarSystemBlob(Small);

        Assert.Equal(Golden, Kit.ToHex(blob));
        Assert.Equal(Small, StateCodec.DecodeStarSystemBlob(blob));
        Assert.Equal(2 + 8 + 1 + 8 + 4 + 1 + (2 * (8 + 1 + 56)), blob.Length);
    }

    [Fact]
    public void StarSystemBlob_GeneratedSystems_RoundTrip()
    {
        for (ulong seed = 0; seed < 50; seed++)
        {
            var system = StarSystemGenesis.CreateWorldBodies(seed, "core:planet/1").System;
            Assert.Equal(system, StateCodec.DecodeStarSystemBlob(StateCodec.EncodeStarSystemBlob(system)));
        }
    }

    public static TheoryData<string, byte[]> Damaged()
    {
        var valid = StateCodec.EncodeStarSystemBlob(Small);
        const int count = 2 + 8 + 1 + 8 + 4;
        const int body1 = count + 1 + 65;
        byte[] With(int index, params byte[] values)
        {
            var copy = (byte[])valid.Clone();
            values.CopyTo(copy, index);
            return copy;
        }

        var nan = BitConverter.GetBytes(double.NaN);
        var infinity = BitConverter.GetBytes(double.PositiveInfinity);
        return new TheoryData<string, byte[]>
        {
            { "empty", [] },
            { "wrong kind", With(0, (byte)BlobKind.PlanetParams) },
            { "unknown format", With(1, 2) },
            { "truncated", valid[..^1] },
            { "trailing byte", [.. valid, 0] },
            { "count 0", [.. valid[..count], 0] },
            { "count 9", With(count, 9) },
            { "count above the bodies", With(count, 3) },
            { "nan position", With(body1 + 9, nan) },
            { "infinite rotation", With(body1 + 9 + 24, infinity) },
            { "nan sun cycle", With(count - 4, BitConverter.GetBytes(float.NaN)) },
            { "zero sun cycle", With(count - 4, 0, 0, 0, 0) },
            { "duplicate planet", With(body1, 0x02, 0x01) },
            { "duplicate index", With(body1 + 8, 0) },
            { "home missing", With(2 + 8 + 1, 0x09) },
            { "home off the origin", With(count + 1 + 9, BitConverter.GetBytes(1.0)) },
            { "unnormalised rotation", With(body1 + 9 + 24 + 24, BitConverter.GetBytes(0.5)) },
            { "system 0", With(2, 0) },
        };
    }

    [Theory]
    [MemberData(nameof(Damaged))]
    public void StarSystemBlob_Damaged_ThrowsProtocolViolation(string name, byte[] blob)
    {
        Assert.NotNull(name);
        Assert.Throws<ProtocolViolationException>(() => StateCodec.DecodeStarSystemBlob(blob));
    }

    [Fact]
    public void StarSystemBlob_EncodeInvalid_Throws()
    {
        Assert.Throws<ArgumentException>(() => StateCodec.EncodeStarSystemBlob(Small with { Bodies = [] }));
    }

    // Approved by Opus in the PR (plan step 3).
    private const string Golden =
        "0A 01 01 00 00 00 00 00 00 00 01 02 01 00 00 00 00 00 00 00 00 E1 44 02 "
        + "02 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 F0 3F "
        + "0B 0A 00 00 00 00 00 00 01 00 00 00 00 00 94 D1 40 00 00 00 00 00 00 F0 BF 00 00 00 00 00 00 04 40 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 F0 3F";
}
