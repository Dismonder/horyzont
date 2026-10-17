using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// K6-35 B1 (approved at the review of gate A): the binary contracts of the package. Operation 7 in format 2 (a final byte 01 for the reset of the vitals; format 1 byte for byte as before),
/// operations 38, 39 and 46, blobs 23 and 24 (the content of 46 and of 38 behind the two header bytes), message 47, the tail of message 44 (tick, counter) and the tail of message 48
/// (bed, transition, page of tombstones, death notice). Every refusal test first decodes the valid twin.
/// </summary>
public sealed class DeathCodecTests
{
    private const string Position123 = "00 00 80 3F 00 00 00 40 00 00 40 40";
    private const string RotationIdentity = "00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F";
    private const string VelocityZero = "00 00 00 00 00 00 00 00 00 00 00 00";

    // Entity 7 to planet 1, counter 1, position (1, 2, 3), identity rotation, no velocity, no seat.
    private const string ReparentV1 = "07 01 07 02 01 01 " + Position123 + " " + RotationIdentity + " " + VelocityZero + " 00";
    private const string ReparentV2 = "07 02 07 02 01 01 " + Position123 + " " + RotationIdentity + " " + VelocityZero + " 00 01";

    // Entity 9, a tombstone (6), planet 1, counter 0, position (1, 2, 3), identity rotation, owner 4.
    private const string EntityBody = "09 06 02 01 00 " + Position123 + " " + RotationIdentity + " 04";

    // Teleport everything and free building, 150 percent resources, 200 stacks, 75 hazards, relaxed integrity, the hard penalty.
    private const string SettingsBody = "05 96 00 C8 00 4B 00 02 03";

    private static readonly EntityReparentOp _reparent = new(
        new EntityId(7), FrameRef.Of(new PlanetId(1)), 1, new Vector3(1f, 2f, 3f), Quaternion.Identity, Vector3.Zero, null);

    private static readonly PersistentEntityCreateOp _create = new(
        new EntityId(9), PersistentEntityKind.Tombstone, FrameRef.Of(new PlanetId(1)), 0, new Vector3(1f, 2f, 3f), Quaternion.Identity, new PlayerId(4));

    private static readonly WorldSettings _settings = new()
    {
        TeleportAll = true,
        FreeBuilding = true,
        ResourceMultiplierPercent = 150,
        StackMultiplierPercent = 200,
        HazardIntensityPercent = 75,
        BuildingIntegrity = BuildingIntegrityMode.Relaxed,
        DeathPenalty = DeathPenalty.Hard,
    };

    private static T RoundTrip<T>(T op)
        where T : Operation
    {
        var back = Assert.IsType<T>(Kit.DecodeOperation(Kit.Encode(op)));
        Assert.Equal(op, back);
        return back;
    }

    [Fact]
    public void ReparentV1_GoldenBytesUnchanged()
    {
        // The reparent without the reset is format 1, byte for byte as before the package.
        Assert.Equal(ReparentV1, Kit.ToHex(Kit.Encode(_reparent)));
        Assert.Equal(_reparent, Kit.DecodeOperation(Kit.Hex(ReparentV1)));

        // A seat and a velocity keep their place in format 1.
        var seated = _reparent with { LocalVelocity = new Vector3(1f, 0f, 0f), SeatCell = new CellPos(1, 2, 3) };
        Assert.Equal(seated, Kit.DecodeOperation(Kit.Encode(seated)));
        Assert.Equal(1, Kit.Encode(seated)[1]);
    }

    [Fact]
    public void ReparentV2_Reset_RoundTrips()
    {
        // 07 = EntityReparent, format 2, the fields of format 1, then the byte 01. The reset is the only difference.
        var reset = _reparent with { ResetVitals = true };
        Assert.Equal(ReparentV2, Kit.ToHex(Kit.Encode(reset)));
        Assert.Equal(reset, Kit.DecodeOperation(Kit.Hex(ReparentV2)));

        var seated = reset with { SeatCell = new CellPos(-1, 0, 5), LocalVelocity = new Vector3(0f, -1f, 0f) };
        Assert.Equal(seated, Kit.DecodeOperation(Kit.Encode(seated)));

        // In a compound the component keeps its own version byte, and the compound stays format 1.
        var compound = new CompoundOp([new FoodSetOp(new PlayerId(1), default), reset]);
        var frame = Kit.Encode(compound);
        Assert.Equal(1, frame[1]);
        Assert.Equal(compound.Components, Assert.IsType<CompoundOp>(Kit.DecodeOperation(frame)).Components);
        Assert.EndsWith(ReparentV2, Kit.ToHex(frame));
    }

    [Fact]
    public void ReparentV2_ByteOtherThanOne_IsRefused()
    {
        Assert.IsType<EntityReparentOp>(Kit.DecodeOperation(Kit.Hex(ReparentV2)));

        // One encoding per operation: 00 (a format 2 frame that says "no reset") is refused as well as every value above 1.
        Kit.AssertOperationViolation(ReparentV2[..^2] + "00");
        Kit.AssertOperationViolation(ReparentV2[..^2] + "02");
        Kit.AssertOperationViolation(ReparentV2[..^2] + "FF");

        // The reset byte is missing, an extra byte follows, or the format is one that does not exist.
        Kit.AssertOperationViolation(ReparentV2[..^3]);
        Kit.AssertOperationViolation(ReparentV2 + " 00");
        Kit.AssertOperationViolation(ReparentV1 + " 01");
        Kit.AssertOperationViolation("07 03" + ReparentV2[5..]);
        Kit.AssertOperationViolation("07 00" + ReparentV2[5..]);
    }

    [Fact]
    public void OtherOperations_FormatTwo_IsStillRefused()
    {
        // The table of allowed versions is per kind: only operation 7 has format 2.
        Assert.IsType<FoodSetOp>(Kit.DecodeOperation(Kit.Hex("29 01 04 00 00 00")));
        Kit.AssertOperationViolation("29 02 04 00 00 00");
        Kit.AssertOperationViolation("26 02 " + EntityBody);
        Kit.AssertOperationViolation("27 02 09");
        Kit.AssertOperationViolation("2E 02 " + SettingsBody + " 01");
        Kit.AssertOperationViolation("1E 02 01");
    }

    [Fact]
    public void NewOperations_GoldenBytesAndRoundTrip()
    {
        // 0x26 = 38: id, kind, frame (kind byte then id), counter, position, rotation, owner.
        Assert.Equal("26 01 " + EntityBody, Kit.ToHex(Kit.Encode(_create)));
        RoundTrip(_create);
        RoundTrip(_create with { EntityKind = PersistentEntityKind.DroppedItem });
        RoundTrip(_create with { ParentSeq = uint.MaxValue, Owner = new PlayerId(ulong.MaxValue), Entity = new EntityId(ulong.MaxValue) });

        // 0x27 = 39: the id only.
        Assert.Equal("27 01 09", Kit.ToHex(Kit.Encode(new PersistentEntityRemoveOp(new EntityId(9)))));
        RoundTrip(new PersistentEntityRemoveOp(new EntityId(9)));
        RoundTrip(new PersistentEntityRemoveOp(new EntityId(ulong.MaxValue)));

        // 0x2E = 46: flags, three u16 (little endian), the integrity, the penalty.
        Assert.Equal("2E 01 " + SettingsBody, Kit.ToHex(Kit.Encode(new WorldSettingsSetOp(_settings))));
        RoundTrip(new WorldSettingsSetOp(_settings));
        RoundTrip(new WorldSettingsSetOp(WorldSettings.Default));
        Assert.Equal("2E 01 00 64 00 64 00 64 00 01 02", Kit.ToHex(Kit.Encode(new WorldSettingsSetOp(WorldSettings.Default))));

        // The three operations are components of a compound, a death in the shape the builder makes it.
        RoundTrip(new CompoundOp([_create, new PersistentEntityRemoveOp(new EntityId(9)), new WorldSettingsSetOp(_settings), _reparent with { ResetVitals = true }]));
    }

    [Fact]
    public void NewOperationsAndBlobs_RoundTrip()
    {
        // Blob 23 = 0x17: the settings behind the two header bytes. Blob 24 = 0x18: the entity of operation 38 without its header.
        var settingsBlob = StateCodec.EncodeWorldSettingsBlob(_settings);
        Assert.Equal("17 01 " + SettingsBody, Kit.ToHex(settingsBlob));
        Assert.Equal(_settings, StateCodec.DecodeWorldSettingsBlob(settingsBlob));
        Assert.Equal(WorldSettings.Default, StateCodec.DecodeWorldSettingsBlob(StateCodec.EncodeWorldSettingsBlob(WorldSettings.Default)));

        var entity = new PersistentEntity(_create.Entity, _create.EntityKind, _create.Frame, _create.ParentSeq, _create.Position, _create.Rotation, _create.Owner);
        var entityBlob = StateCodec.EncodePersistentEntityBlob(entity);
        Assert.Equal("18 01 " + EntityBody, Kit.ToHex(entityBlob));
        Assert.Equal(entity, StateCodec.DecodePersistentEntityBlob(entityBlob));
        var dropped = entity with { Kind = PersistentEntityKind.DroppedItem, ParentSeq = 77 };
        Assert.Equal(dropped, StateCodec.DecodePersistentEntityBlob(StateCodec.EncodePersistentEntityBlob(dropped)));

        // Message 47 = 0x2F: the same settings content behind the type and the format byte.
        var message = new WorldSettingsState(_settings);
        Assert.Equal("2F 01 " + SettingsBody, Kit.ToHex(Kit.Encode(message)));
        Assert.Equal(message, MessageCodec.Read(Kit.Encode(message)));
        Assert.Equal(DeliveryChannel.ReliableOrdered, MessageChannels.For(MessageType.WorldSettingsState));
    }

    [Fact]
    public void WorldSettings_RangesAndReservedBits()
    {
        Assert.IsType<WorldSettingsSetOp>(Kit.DecodeOperation(Kit.Hex("2E 01 " + SettingsBody)));
        Assert.IsType<WorldSettingsState>(MessageCodec.Read(Kit.Hex("2F 01 " + SettingsBody)));
        StateCodec.DecodeWorldSettingsBlob(Kit.Hex("17 01 " + SettingsBody));

        // The reserved bits of hunger and raids (3 and 4) and every other bit above the three known ones.
        foreach (var flags in new[] { "08", "10", "18", "20", "40", "80", "FF" })
        {
            var body = flags + SettingsBody[2..];
            Kit.AssertOperationViolation("2E 01 " + body);
            Kit.AssertMessageViolation("2F 01 " + body);
            Kit.AssertViolation(d => StateCodec.DecodeWorldSettingsBlob(d), Kit.Hex("17 01 " + body));
        }

        // The ranges: resources 50 to 300, stacks 100 to 300, hazards 50 to 200, integrity 1 to 3, penalty 1 to 3; the neighbours inside are accepted.
        string[] bad =
        [
            "05 31 00 C8 00 4B 00 02 03", // resources 49
            "05 2D 01 C8 00 4B 00 02 03", // resources 301
            "05 96 00 63 00 4B 00 02 03", // stacks 99
            "05 96 00 2D 01 4B 00 02 03", // stacks 301
            "05 96 00 C8 00 31 00 02 03", // hazards 49
            "05 96 00 C8 00 C9 00 02 03", // hazards 201
            "05 96 00 C8 00 4B 00 00 03", // integrity 0
            "05 96 00 C8 00 4B 00 04 03", // integrity 4
            "05 96 00 C8 00 4B 00 02 00", // penalty 0
            "05 96 00 C8 00 4B 00 02 04", // penalty 4
            "05 96 00 C8 00 4B 00 02 FF", // penalty 255
        ];
        foreach (var body in bad)
        {
            Kit.AssertOperationViolation("2E 01 " + body);
            Kit.AssertMessageViolation("2F 01 " + body);
            Kit.AssertViolation(d => StateCodec.DecodeWorldSettingsBlob(d), Kit.Hex("17 01 " + body));
        }

        string[] edges = ["05 32 00 64 00 32 00 01 01", "05 2C 01 2C 01 C8 00 03 03"];
        foreach (var body in edges)
        {
            Assert.IsType<WorldSettingsSetOp>(Kit.DecodeOperation(Kit.Hex("2E 01 " + body)));
        }

        // The writers refuse what the readers refuse.
        foreach (var settings in new[]
        {
            new WorldSettings { ResourceMultiplierPercent = 49 }, new WorldSettings { StackMultiplierPercent = 99 }, new WorldSettings { HazardIntensityPercent = 201 },
            new WorldSettings { BuildingIntegrity = (BuildingIntegrityMode)0 }, new WorldSettings { DeathPenalty = (DeathPenalty)4 },
        })
        {
            Assert.Throws<ArgumentException>(() => Kit.Encode(new WorldSettingsSetOp(settings)));
            Assert.Throws<ArgumentException>(() => Kit.Encode(new WorldSettingsState(settings)));
            Assert.Throws<ArgumentException>(() => StateCodec.EncodeWorldSettingsBlob(settings));
        }
    }

    [Fact]
    public void PersistentEntity_KindFrameAndNumbers_AreChecked()
    {
        Assert.IsType<PersistentEntityCreateOp>(Kit.DecodeOperation(Kit.Hex("26 01 " + EntityBody)));

        // Kinds other than 3 and 6, the frame kind 0 or 4, a position that is not finite, a rotation that is not a unit quaternion.
        foreach (var kind in new[] { "00", "01", "02", "04", "05", "07", "FF" })
        {
            Kit.AssertOperationViolation("26 01 09 " + kind + EntityBody[5..]);
        }

        Kit.AssertOperationViolation("26 01 09 06 00 01 00 " + Position123 + " " + RotationIdentity + " 04");
        Kit.AssertOperationViolation("26 01 09 06 04 01 00 " + Position123 + " " + RotationIdentity + " 04");
        Kit.AssertOperationViolation("26 01 09 06 02 01 00 00 00 C0 7F 00 00 00 40 00 00 40 40 " + RotationIdentity + " 04");
        Kit.AssertOperationViolation("26 01 09 06 02 01 00 " + Position123 + " 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 40 04");
        Kit.AssertViolation(d => StateCodec.DecodePersistentEntityBlob(d), Kit.Hex("18 01 09 05" + EntityBody[5..]));
        Kit.AssertViolation(d => StateCodec.DecodePersistentEntityBlob(d), Kit.Hex("18 01 " + EntityBody + " 00"));
        Kit.AssertViolation(d => StateCodec.DecodePersistentEntityBlob(d), Kit.Hex("18 02 " + EntityBody));

        Assert.Throws<ArgumentException>(() => Kit.Encode(_create with { EntityKind = (PersistentEntityKind)5 }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(_create with { Position = new Vector3(float.NaN, 0f, 0f) }));
    }

    [Fact]
    public void MalformedFrames_RejectBeforeAllocation()
    {
        // Every proper prefix of every frame of the package is a violation, never another exception and never a success.
        byte[][] frames =
        [
            Kit.Hex(ReparentV2),
            Kit.Hex("26 01 " + EntityBody),
            Kit.Hex("27 01 09"),
            Kit.Hex("2E 01 " + SettingsBody),
            Kit.Hex("17 01 " + SettingsBody),
            Kit.Hex("18 01 " + EntityBody),
            Kit.Hex("2F 01 " + SettingsBody),
        ];
        var decoders = new Func<byte[], object>[]
        {
            d => Kit.DecodeOperation(d), d => Kit.DecodeOperation(d), d => Kit.DecodeOperation(d), d => Kit.DecodeOperation(d),
            d => StateCodec.DecodeWorldSettingsBlob(d), d => StateCodec.DecodePersistentEntityBlob(d), d => MessageCodec.Read(d),
        };
        Assert.Equal(frames.Length, decoders.Length);
        for (var f = 0; f < frames.Length; f++)
        {
            var decode = decoders[f];
            Assert.NotNull(decode(frames[f]));
            foreach (var length in Kit.PrefixLengths(frames[f].Length))
            {
                Kit.AssertViolation(d => decode(d.ToArray()), frames[f][..length]);
            }
        }

        // Damaged copies of the valid frames: a violation or another valid value, nothing else.
        for (var seed = 0; seed < 300; seed++)
        {
            foreach (var frame in frames.Take(4))
            {
                try
                {
                    Kit.DecodeOperation(Kit.Mutate(frame, seed));
                }
                catch (ProtocolViolationException)
                {
                    // Expected for most damaged frames.
                }
            }
        }

        // A compound that declares thousands of components and holds a few bytes fails on the count, before anything is allocated.
        Kit.AssertOperationViolation("1E 01 FF 1F 26 01");
    }

    [Fact]
    public void VitalsMessage_Tail_IsTickAndCounter()
    {
        var message = new PlayerVitalsMessage(
            80f, 100f, 50f, 100f, false, false, false, 0, 0, 0, VitalsEnvironment.None, PlayerVitalsMessage.DefaultSuit, PlayerVitalsMessage.DefaultSuit, new Tick(300), 5);
        var neutral = message with { SampleTick = default, ParentSeq = 0 };

        var frame = Kit.ToHex(Kit.Encode(message));
        var neutralFrame = Kit.ToHex(Kit.Encode(neutral));

        // Two varuints after the suit tail of K6-34: the tick (300 = AC 02) and the counter.
        Assert.EndsWith("AC 02 05", frame);
        Assert.EndsWith("00 00", neutralFrame);
        Assert.Equal(neutralFrame[..^5], frame[..^8]);
        DeepAssert.Equal(message, MessageCodec.Read(Kit.Encode(message)));

        var max = message with { SampleTick = new Tick(ulong.MaxValue), ParentSeq = uint.MaxValue };
        DeepAssert.Equal(max, MessageCodec.Read(Kit.Encode(max)));

        // The tail is required: a frame without it, or with a counter above 32 bits, is a violation.
        var bytes = Kit.Encode(neutral);
        Kit.AssertViolation(d => MessageCodec.Read(d), bytes[..^2]);
        Kit.AssertViolation(d => MessageCodec.Read(d), bytes[..^1]);
        Kit.AssertViolation(d => MessageCodec.Read(d), [.. bytes[..^1], 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01]);
    }

    private static PlayerProgressState State(PlayerRespawnState respawn) => new(ItemTypeBitSet.Empty, [], FoodSlots.Empty, respawn);

    private static TombstoneEntry Entry(ulong entity) =>
        new(new EntityId(entity), new ContainerId(entity + 1), FrameRef.Of(new PlanetId(1)), 2, new Vector3(1f, 2f, 3f));

    [Fact]
    public void ProgressMessage_Tail_GoldenBytes()
    {
        // 48 = 0x30. The part up to the tail is that of K6-33 (empty bitset, no keys, empty food); the empty tail is twelve bytes: no bed, no transition, revision 0,
        // page 0 of 1, no entries, no death.
        var empty = Kit.ToHex(Kit.Encode(new PlayerProgressState(ItemTypeBitSet.Empty, [], FoodSlots.Empty)));
        Assert.Equal("30 01 00 00 00 00 00 00 00 00 00 00 00 00 01 00 00 00", empty);
        Assert.Equal(empty, Kit.ToHex(Kit.Encode(State(PlayerRespawnState.None))));

        // Bed (construct 5, cell (1, 2, 3), type 7), the respawn transition, revision 3, page 1 of 2, one entry (entity 9, container 10, planet 1, counter 2, position),
        // the death notice (operation 77, counter 5).
        var full = new PlayerRespawnState(
            new BedRef(new EntityId(5), new CellPos(1, 2, 3), 7), PlayerTransitionKind.Respawn, 3, 1, 2, [Entry(9) with { Container = new ContainerId(10) }], new DeathNotice(new OpId(77), 5));
        Assert.Equal(
            "30 01 00 00 00 00 00 " + "01 05 02 04 06 07 " + "01 " + "03 00 00 00 " + "01 00 02 00 " + "01 09 0A 02 01 02 " + Position123 + " " + "01 4D 05",
            Kit.ToHex(Kit.Encode(State(full))));
        DeepAssert.Equal(State(full), MessageCodec.Read(Kit.Encode(State(full))));
    }

    [Fact]
    public void ProgressMessage_Tail_RoundTripsEveryShape()
    {
        PlayerRespawnState[] shapes =
        [
            PlayerRespawnState.None,
            PlayerRespawnState.None with { Transition = PlayerTransitionKind.Respawn },
            PlayerRespawnState.None with { Bed = new BedRef(new EntityId(ulong.MaxValue), new CellPos(-5, 0, 9), ushort.MaxValue) },
            PlayerRespawnState.None with { Death = new DeathNotice(new OpId(ulong.MaxValue), uint.MaxValue) },
            new PlayerRespawnState(null, PlayerTransitionKind.None, uint.MaxValue, 0, 1, [Entry(1)], null),
            new PlayerRespawnState(null, PlayerTransitionKind.None, 1, ushort.MaxValue - 1, ushort.MaxValue, [], null),
            new PlayerRespawnState(null, PlayerTransitionKind.None, 9, 0, 1, [.. Enumerable.Range(1, ProtocolConstants.MaxTombstonesPerPage).Select(i => Entry((ulong)i * 3))], null),
        ];
        Assert.Equal(7, shapes.Length);
        foreach (var shape in shapes)
        {
            DeepAssert.Equal(State(shape), MessageCodec.Read(Kit.Encode(State(shape))));
        }
    }

    [Fact]
    public void MalformedFrames_ProgressTail_RejectBeforeAllocation()
    {
        var valid = new PlayerRespawnState(
            new BedRef(new EntityId(5), new CellPos(1, 2, 3), 7), PlayerTransitionKind.Respawn, 3, 1, 2, [Entry(9)], new DeathNotice(new OpId(77), 5));
        var bytes = Kit.Encode(State(valid));
        Assert.IsType<PlayerProgressState>(MessageCodec.Read(bytes));
        foreach (var length in Kit.PrefixLengths(bytes.Length))
        {
            Kit.AssertViolation(d => MessageCodec.Read(d), bytes[..length]);
        }

        // The tail starts after the seven bytes of the empty head. Offsets in the empty tail: bed 0, transition 1, revision 2..5, page 6..7, page count 8..9, count 10, death 11.
        var empty = Kit.Encode(State(PlayerRespawnState.None));
        const int tail = 7;
        Assert.Equal(tail + 12, empty.Length);

        void Violation(Action<byte[]> damage, string why)
        {
            var copy = empty.ToArray();
            damage(copy);
            try
            {
                MessageCodec.Read(copy);
                Assert.Fail("Accepted: " + why);
            }
            catch (ProtocolViolationException)
            {
                // Expected.
            }
        }

        Violation(b => b[tail + 0] = 2, "bed flag 2");
        Violation(b => b[tail + 1] = 2, "transition 2 is reserved for the teleport");
        Violation(b => b[tail + 1] = 3, "transition 3");
        Violation(b => b[tail + 8] = 0, "page count 0");
        Violation(b => b[tail + 6] = 1, "page 1 of 1");
        Violation(b => b[tail + 10] = 65, "65 entries");
        Violation(b => b[tail + 10] = 64, "64 entries declared and none present");
        Violation(b => b[tail + 11] = 2, "death flag 2");
        Violation(b => b[tail + 11] = 1, "death flag without a notice");

        // The same refusals on the writer.
        Assert.Throws<ArgumentException>(() => Kit.Encode(State(PlayerRespawnState.None with { PageCount = 0 })));
        Assert.Throws<ArgumentException>(() => Kit.Encode(State(PlayerRespawnState.None with { Page = 1 })));
        Assert.Throws<ArgumentException>(() => Kit.Encode(State(PlayerRespawnState.None with { Transition = (PlayerTransitionKind)2 })));
        Assert.Throws<ArgumentException>(() => Kit.Encode(State(PlayerRespawnState.None with { Tombstones = [.. Enumerable.Range(1, 65).Select(i => Entry((ulong)i))] })));
        Assert.Throws<ArgumentException>(() => Kit.Encode(State(PlayerRespawnState.None with { Tombstones = [Entry(1) with { Position = new Vector3(float.NaN, 0f, 0f) }] })));
    }
}
