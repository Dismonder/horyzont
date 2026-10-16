using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Serialization;
using Horyzont.Core.World;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// K6-33, D5: blob 2 (a player) in version 2 appends the vitals to the bytes of version 1: a flag byte (bit 0 vitals present, bit 1 exhausted) and, with bit 0,
/// two float32 (health, stamina, little endian) and a varuint (the tick the rested bonus ends at, 0 for none). The record without a prefix
/// (<c>WritePlayer</c>, <c>ReadPlayer</c>) stays the bytes of version 1; only the blob carries the vitals.
/// </summary>
public sealed class StateCodecVitalsTests
{
    private const string Zero12 = "00 00 00 00 00 00 00 00 00 00 00 00";
    private const string IdentityRotation = "00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F";

    /// <summary>The bytes of a player record of version 1: player 7, platform 2, name "a", entity 9, frame 2/9, seat none, origin, online.</summary>
    private const string Body = "07 02 44 33 22 11 00 00 00 00 01 61 09 02 09 00 00 " + Zero12 + " " + IdentityRotation + " 00 00 00 00 01";

    private static readonly PlayerRecord _player = Kit.ReadAll(Kit.Hex(Body), (ref ByteReader r) => StateCodec.ReadPlayer(ref r));

    private static byte[] Blob(byte version, string flagsAndVitals) => Kit.Hex("02 " + version.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + " " + Body + " " + flagsAndVitals);

    private static byte[] WithVitals(byte flags, float health, float stamina, string rested = "00") =>
        Kit.Concat(Kit.Hex("02 02 " + Body + " " + flags.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)), BitConverter.GetBytes(health), BitConverter.GetBytes(stamina), Kit.Hex(rested));

    [Fact]
    public void PlayerBlob_V2_GoldenBytes()
    {
        // 80 = 00 00 A0 42, 55.5 = 00 00 5E 42, 36000 = A0 99 02 as varuint.
        var vitals = new PlayerVitals(80f, 55.5f, new Tick(36000), false);
        Assert.Equal("02 02 " + Body + " 01 00 00 A0 42 00 00 5E 42 A0 99 02", Kit.ToHex(StateCodec.EncodePlayerBlob(_player with { Vitals = vitals })));

        // Exhausted: bit 1 as well; health 1 = 00 00 80 3F, stamina 0, no rested bonus.
        var exhausted = new PlayerVitals(1f, 0f, default, true);
        Assert.Equal("02 02 " + Body + " 03 00 00 80 3F 00 00 00 00 00", Kit.ToHex(StateCodec.EncodePlayerBlob(_player with { Vitals = exhausted })));

        // No vitals: the flag byte 00 and nothing after it.
        Assert.Equal("02 02 " + Body + " 00", Kit.ToHex(StateCodec.EncodePlayerBlob(_player)));
    }

    [Fact]
    public void PlayerBlob_V2_RoundTripWithVitals()
    {
        foreach (var vitals in new[]
        {
            new PlayerVitals(80f, 55.5f, new Tick(36000), false),
            new PlayerVitals(0f, 0f, default, true),
            new PlayerVitals(10_000f, 10_000f, new Tick(ulong.MaxValue), false),
            new PlayerVitals(0.25f, 99.75f, new Tick(1), true),
        })
        {
            var record = _player with { Vitals = vitals };
            var blob = StateCodec.EncodePlayerBlob(record);
            Assert.Equal(2, blob[1]);
            DeepAssert.Equal(record, StateCodec.DecodePlayerBlob(blob));
            Assert.Equal(vitals, StateCodec.DecodePlayerBlob(blob).Vitals);
            Assert.Equal(blob, StateCodec.EncodePlayerBlob(StateCodec.DecodePlayerBlob(blob)));
        }
    }

    [Fact]
    public void PlayerBlob_V2_WithoutVitals()
    {
        var blob = StateCodec.EncodePlayerBlob(_player);

        Assert.Equal(2, blob[1]);
        Assert.Null(StateCodec.DecodePlayerBlob(blob).Vitals);
        DeepAssert.Equal(_player, StateCodec.DecodePlayerBlob(blob));
    }

    [Fact]
    public void PlayerBlob_V1_VitalsNull()
    {
        // The bytes of version 1 (K5 and later worlds before K6-33) still read: the record has no vitals.
        var v1 = Kit.Hex("02 01 " + Body);

        var record = StateCodec.DecodePlayerBlob(v1);

        Assert.Null(record.Vitals);
        DeepAssert.Equal(_player, record);

        // Version 1 has no flag byte: one more byte after the record is a trailing byte, not a flag.
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), Kit.Hex("02 01 " + Body + " 00"));
    }

    [Fact]
    public void PlayerBlob_NaNOrInfinity_Throws()
    {
        // The accepted counterpart first.
        Assert.Equal(new PlayerVitals(80f, 55.5f, default, false), StateCodec.DecodePlayerBlob(WithVitals(1, 80f, 55.5f)).Vitals);

        foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), WithVitals(1, bad, 55.5f));
            Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), WithVitals(1, 80f, bad));
            Assert.Throws<ArgumentException>(() => StateCodec.EncodePlayerBlob(_player with { Vitals = new PlayerVitals(bad, 55.5f, default, false) }));
            Assert.Throws<ArgumentException>(() => StateCodec.EncodePlayerBlob(_player with { Vitals = new PlayerVitals(80f, bad, default, false) }));
        }
    }

    [Fact]
    public void PlayerBlob_NegativeOrAboveMax_Throws()
    {
        // The limits themselves are valid: 0 and 10000.
        Assert.Equal(new PlayerVitals(0f, 10_000f, default, true), StateCodec.DecodePlayerBlob(WithVitals(3, 0f, 10_000f)).Vitals);

        foreach (var bad in new[] { -1f, -0.001f, 10_001f, 10_000.5f, float.MaxValue })
        {
            Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), WithVitals(1, bad, 55.5f));
            Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), WithVitals(1, 80f, bad));
            Assert.Throws<ArgumentException>(() => StateCodec.EncodePlayerBlob(_player with { Vitals = new PlayerVitals(bad, 55.5f, default, false) }));
            Assert.Throws<ArgumentException>(() => StateCodec.EncodePlayerBlob(_player with { Vitals = new PlayerVitals(80f, bad, default, false) }));
        }
    }

    [Fact]
    public void PlayerBlob_ExhaustedWithoutVitals_Throws()
    {
        // The accepted counterparts: no vitals (flag 00), vitals (flag 01), exhausted vitals (flag 03).
        Assert.Null(StateCodec.DecodePlayerBlob(Blob(2, "00")).Vitals);
        Assert.NotNull(StateCodec.DecodePlayerBlob(WithVitals(1, 80f, 55.5f)).Vitals);
        Assert.True(StateCodec.DecodePlayerBlob(WithVitals(3, 80f, 55.5f)).Vitals!.Value.Exhausted);

        // Bit 1 without bit 0 has nothing to be a flag of.
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), Blob(2, "02"));
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), WithVitals(2, 80f, 55.5f));
    }

    [Fact]
    public void PlayerBlob_UnknownFlag_Throws()
    {
        Assert.NotNull(StateCodec.DecodePlayerBlob(WithVitals(1, 80f, 55.5f)).Vitals);

        foreach (byte flags in new byte[] { 4, 5, 7, 8, 0x80, 0xFF })
        {
            Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), WithVitals(flags, 80f, 55.5f));
        }

        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), Blob(2, "04"));
    }

    [Fact]
    public void PlayerBlob_Truncated_Throws()
    {
        var full = WithVitals(1, 80f, 55.5f);
        Assert.NotNull(StateCodec.DecodePlayerBlob(full).Vitals);

        // Missing flag byte, missing floats, missing varuint, and a trailing byte.
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), Kit.Hex("02 02 " + Body));
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), full[..^1]);
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), full[..^5]);
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), Kit.Concat(full, [0]));
    }

    [Fact]
    public void PlayerBlob_Version3_Throws()
    {
        Assert.Null(StateCodec.DecodePlayerBlob(Blob(2, "00")).Vitals);

        // Version 3 is K6-34 (suit resources), not known to this build.
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), Blob(3, "00"));
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), Blob(0, "00"));
    }

    [Fact]
    public void PlayerRecordWithoutPrefix_StaysVersionOne()
    {
        // WritePlayer and ReadPlayer are the bytes of version 1 (the snapshot of a record is not a blob); the vitals are never written there.
        var withVitals = _player with { Vitals = new PlayerVitals(80f, 55.5f, new Tick(36000), false) };

        var bytes = Kit.Write(w => StateCodec.WritePlayer(w, withVitals));

        Assert.Equal(Body, Kit.ToHex(bytes));
        Assert.Null(Kit.ReadAll(bytes, (ref ByteReader r) => StateCodec.ReadPlayer(ref r)).Vitals);
    }
}
