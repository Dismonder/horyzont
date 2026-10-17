using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Serialization;
using Horyzont.Core.Survival;
using Horyzont.Core.World;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// K6-34, D4: blob 2 (a player) in version 3 appends the suit to the vitals of version 2: bit 2 of the flag byte ("suit present", needs bit 0) and, with it, five float32 (heating, cooling,
/// filter, shield, oxygen; little endian) after the vitals. The mask of known flags is 3 in version 2 and 7 in version 3. The record without a prefix (<c>WritePlayer</c>, <c>ReadPlayer</c>)
/// stays the bytes of version 1.
/// </summary>
public sealed class StateCodecSuitTests
{
    private const string Zero12 = "00 00 00 00 00 00 00 00 00 00 00 00";
    private const string IdentityRotation = "00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F";

    /// <summary>The bytes of a player record of version 1: player 7, platform 2, name "a", entity 9, frame 2/9, seat none, origin, online.</summary>
    private const string Body = "07 02 44 33 22 11 00 00 00 00 01 61 09 02 09 00 00 " + Zero12 + " " + IdentityRotation + " 00 00 00 00 01";

    /// <summary>Vitals 80, 55.5, rested until 36000: the bytes after the flag byte.</summary>
    private const string VitalsBytes = "00 00 A0 42 00 00 5E 42 A0 99 02";

    /// <summary>Heating 100, cooling 50, filter 0, shield 25, oxygen 75.5.</summary>
    private const string SuitBytes = "00 00 C8 42 00 00 48 42 00 00 00 00 00 00 C8 41 00 00 97 42";

    private static readonly SuitResources _suit = new(100f, 50f, 0f, 25f, 75.5f);

    private static readonly PlayerRecord _player = Kit.ReadAll(Kit.Hex(Body), (ref ByteReader r) => StateCodec.ReadPlayer(ref r));

    private static readonly PlayerVitals _vitals = new(80f, 55.5f, new Tick(36000), false, _suit);

    private static string Hex(byte value) => value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture);

    private static byte[] Blob(byte version, string rest) => Kit.Hex("02 " + Hex(version) + " " + Body + " " + rest);

    private static byte[] WithSuit(byte version, byte flags, params float[] suit) =>
        Kit.Concat(Kit.Hex("02 " + Hex(version) + " " + Body + " " + Hex(flags) + " " + VitalsBytes), suit.SelectMany(BitConverter.GetBytes).ToArray());

    [Fact]
    public void PlayerBlob_V3_GoldenBytesWithSuit()
    {
        // Flag 05 = vitals (bit 0) and suit (bit 2); then the vitals of version 2 and the five resources in the order of SuitResource.
        Assert.Equal("02 03 " + Body + " 05 " + VitalsBytes + " " + SuitBytes, Kit.ToHex(StateCodec.EncodePlayerBlob(_player with { Vitals = _vitals })));

        // Exhausted with a suit: bits 0, 1 and 2 = 07; health 1, stamina 0, no rested bonus, a full suit of 100 (00 00 C8 42).
        var full = new SuitResources(100f, 100f, 100f, 100f, 100f);
        Assert.Equal(
            "02 03 " + Body + " 07 00 00 80 3F 00 00 00 00 00 00 00 C8 42 00 00 C8 42 00 00 C8 42 00 00 C8 42 00 00 C8 42",
            Kit.ToHex(StateCodec.EncodePlayerBlob(_player with { Vitals = new PlayerVitals(1f, 0f, default, true, full) })));
    }

    [Fact]
    public void PlayerBlob_V3_WithoutSuit()
    {
        // The vitals of version 2 with the same bytes and the new version byte; a record without vitals is the flag byte 00.
        Assert.Equal("02 03 " + Body + " 01 " + VitalsBytes, Kit.ToHex(StateCodec.EncodePlayerBlob(_player with { Vitals = _vitals with { Suit = null } })));
        Assert.Equal("02 03 " + Body + " 00", Kit.ToHex(StateCodec.EncodePlayerBlob(_player)));
        Assert.Equal("02 03 " + Body + " 03 00 00 80 3F 00 00 00 00 00", Kit.ToHex(StateCodec.EncodePlayerBlob(_player with { Vitals = new PlayerVitals(1f, 0f, default, true) })));
    }

    [Fact]
    public void PlayerBlob_RoundTripAndWritesVersion3()
    {
        foreach (var vitals in new PlayerVitals?[]
        {
            null,
            _vitals,
            _vitals with { Suit = null },
            new PlayerVitals(0f, 0f, default, true, new SuitResources(0f, 0f, 0f, 0f, 0f)),
            new PlayerVitals(10_000f, 10_000f, new Tick(ulong.MaxValue), false, new SuitResources(1000f, 1000f, 1000f, 1000f, 1000f)),
            new PlayerVitals(0.25f, 99.75f, new Tick(1), true, new SuitResources(0.5f, 999.5f, 1f, 1000f, 0.001f)),
        })
        {
            var record = _player with { Vitals = vitals };
            var blob = StateCodec.EncodePlayerBlob(record);
            Assert.Equal(3, blob[1]);
            DeepAssert.Equal(record, StateCodec.DecodePlayerBlob(blob));
            Assert.Equal(vitals, StateCodec.DecodePlayerBlob(blob).Vitals);
            Assert.Equal(blob, StateCodec.EncodePlayerBlob(StateCodec.DecodePlayerBlob(blob)));
        }
    }

    [Fact]
    public void PlayerBlob_ReadsV1V2V3()
    {
        // Version 1 (before K6-33): no vitals.
        Assert.Null(StateCodec.DecodePlayerBlob(Kit.Hex("02 01 " + Body)).Vitals);

        // Version 2 (K6-33): vitals without a suit, as before.
        var v2 = StateCodec.DecodePlayerBlob(Blob(2, "01 " + VitalsBytes)).Vitals;
        Assert.Equal(new PlayerVitals(80f, 55.5f, new Tick(36000), false), v2);
        Assert.Null(v2!.Value.Suit);
        Assert.Null(StateCodec.DecodePlayerBlob(Blob(2, "00")).Vitals);

        // Version 3: vitals with and without the suit.
        Assert.Equal(_vitals, StateCodec.DecodePlayerBlob(Blob(3, "05 " + VitalsBytes + " " + SuitBytes)).Vitals);
        Assert.Equal(_vitals with { Suit = null }, StateCodec.DecodePlayerBlob(Blob(3, "01 " + VitalsBytes)).Vitals);
        Assert.Null(StateCodec.DecodePlayerBlob(Blob(3, "00")).Vitals);
    }

    [Fact]
    public void PlayerBlob_V2_SuitBit_Throws()
    {
        // The accepted counterpart: version 2 with its own flag 01. Bit 2 is a flag of version 3 only.
        Assert.NotNull(StateCodec.DecodePlayerBlob(Blob(2, "01 " + VitalsBytes)).Vitals);

        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), Blob(2, "05 " + VitalsBytes + " " + SuitBytes));
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), Blob(2, "07 " + VitalsBytes + " " + SuitBytes));
    }

    [Fact]
    public void SuitBitWithoutVitals_Throws()
    {
        // The accepted counterparts: flag 00, 01, 05. Bit 2 without bit 0 has no vitals to belong to.
        Assert.Null(StateCodec.DecodePlayerBlob(Blob(3, "00")).Vitals);
        Assert.NotNull(StateCodec.DecodePlayerBlob(WithSuit(3, 5, 100f, 50f, 0f, 25f, 75.5f)).Vitals);

        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), Blob(3, "04"));
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), Blob(3, "04 " + VitalsBytes + " " + SuitBytes));
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), Blob(3, "06 " + VitalsBytes + " " + SuitBytes));

        // Bit 1 without bit 0 stays refused as in version 2.
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), Blob(3, "02"));
    }

    [Fact]
    public void UnknownFlagBit3_Throws()
    {
        Assert.NotNull(StateCodec.DecodePlayerBlob(WithSuit(3, 7, 100f, 50f, 0f, 25f, 75.5f)).Vitals);

        foreach (byte flags in new byte[] { 8, 9, 0x0D, 0x0F, 0x10, 0x80, 0xFF })
        {
            Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), WithSuit(3, flags, 100f, 50f, 0f, 25f, 75.5f));
        }
    }

    [Fact]
    public void SuitNaNNegativeOrAbove1000_Throws()
    {
        // The limits themselves are valid: 0 and 1000.
        Assert.Equal(new SuitResources(0f, 1000f, 0f, 1000f, 0f), StateCodec.DecodePlayerBlob(WithSuit(3, 5, 0f, 1000f, 0f, 1000f, 0f)).Vitals!.Value.Suit);

        foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, -0.001f, 1000.01f, 1001f, float.MaxValue })
        {
            for (var position = 0; position < 5; position++)
            {
                var values = new[] { 100f, 50f, 0f, 25f, 75.5f };
                values[position] = bad;
                Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), WithSuit(3, 5, values));

                var suit = new SuitResources(values[0], values[1], values[2], values[3], values[4]);
                Assert.Throws<ArgumentException>(() => StateCodec.EncodePlayerBlob(_player with { Vitals = _vitals with { Suit = suit } }));
            }
        }
    }

    [Fact]
    public void SuitNegativeZero_WrittenAsPositiveZero()
    {
        // -0 passes the validity check (it equals 0), but the bytes must be canonical: 00 00 00 00, not 00 00 00 80.
        var negative = new SuitResources(-0f, -0f, -0f, -0f, -0f);
        var positive = new SuitResources(0f, 0f, 0f, 0f, 0f);

        var bytes = StateCodec.EncodePlayerBlob(_player with { Vitals = _vitals with { Suit = negative } });

        Assert.Equal(Kit.ToHex(StateCodec.EncodePlayerBlob(_player with { Vitals = _vitals with { Suit = positive } })), Kit.ToHex(bytes));
        Assert.EndsWith("00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00", Kit.ToHex(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void Version4_Throws()
    {
        Assert.Null(StateCodec.DecodePlayerBlob(Blob(3, "00")).Vitals);

        // Version 4 is not known to this build.
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), Blob(4, "00"));
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), Blob(0, "00"));
    }

    [Fact]
    public void PlayerBlob_TruncatedSuit_Throws()
    {
        var full = WithSuit(3, 5, 100f, 50f, 0f, 25f, 75.5f);
        Assert.NotNull(StateCodec.DecodePlayerBlob(full).Vitals!.Value.Suit);

        // A suit that stops inside the five floats, and a trailing byte.
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), full[..^1]);
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), full[..^4]);
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), full[..^19]);
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), Kit.Concat(full, [0]));
    }

    [Fact]
    public void PlayerRecordWithoutPrefix_StaysVersionOne()
    {
        // WritePlayer and ReadPlayer are the bytes of version 1 (the snapshot of a record is not a blob); the vitals and the suit are never written there.
        var bytes = Kit.Write(w => StateCodec.WritePlayer(w, _player with { Vitals = _vitals }));

        Assert.Equal(Body, Kit.ToHex(bytes));
        Assert.Null(Kit.ReadAll(bytes, (ref ByteReader r) => StateCodec.ReadPlayer(ref r)).Vitals);
    }
}
