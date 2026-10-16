using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// Intents 22 (<see cref="QuickStackIntent"/>), 23 (<see cref="TakeAllIntent"/>), 28 (<see cref="BlockInteractIntent"/>) and
/// 29 (<see cref="BlockConfigIntent"/>), format 1 (K6-30). Every refusal test first decodes or encodes the valid twin, because an unknown
/// intent is refused by the codec today and the refusal alone would prove nothing.
/// </summary>
public sealed class IntentCodecBlockTests
{
    private static readonly CellPos _cell = new(1, -1, 0);

    public static TheoryData<Intent, byte> Intents() => new()
    {
        { new QuickStackIntent(), 22 },
        { new TakeAllIntent(new ContainerId(1)), 23 },
        { new TakeAllIntent(new ContainerId(ulong.MaxValue)), 23 },
        { new BlockInteractIntent(new EntityId(1), new CellPos(0, 0, 0), BlockInteractAction.Toggle), 28 },
        { new BlockInteractIntent(new EntityId(70_000), new CellPos(-2048, 2047, -1), BlockInteractAction.Sleep), 28 },
        { new BlockInteractIntent(new EntityId(ulong.MaxValue), new CellPos(2047, -2048, 17), BlockInteractAction.SetRespawn), 28 },
        { new BlockConfigIntent(new EntityId(5), _cell, new ContainerAccessConfig(ContainerAccess.Shared)), 29 },
        { new BlockConfigIntent(new EntityId(5), _cell, new ContainerAccessConfig(ContainerAccess.Private)), 29 },
        { new BlockConfigIntent(new EntityId(5), _cell, new TeleportNameConfig("A")), 29 },
        { new BlockConfigIntent(new EntityId(5), _cell, new TeleportNameConfig(new string('x', 32))), 29 },
        { new BlockConfigIntent(new EntityId(5), _cell, new TeleportNameConfig("Zażółć gęślą")), 29 },
    };

    [Theory]
    [MemberData(nameof(Intents))]
    public void RoundTrip(Intent intent, byte code)
    {
        var bytes = Kit.Encode(intent);

        Assert.Equal(intent, Kit.DecodeIntent(bytes));
        Assert.Equal(code, bytes[0]);
        Assert.Equal(1, bytes[1]);
    }

    [Fact]
    public void GoldenBytes()
    {
        Assert.Equal("16 01", Kit.ToHex(Kit.Encode(new QuickStackIntent())));
        Assert.Equal("17 01 07", Kit.ToHex(Kit.Encode(new TakeAllIntent(new ContainerId(7)))));
        Assert.Equal(
            "1C 01 05 02 01 00 02", Kit.ToHex(Kit.Encode(new BlockInteractIntent(new EntityId(5), _cell, BlockInteractAction.Sleep))));
        Assert.Equal(
            "1D 01 05 02 01 00 02 02",
            Kit.ToHex(Kit.Encode(new BlockConfigIntent(new EntityId(5), _cell, new ContainerAccessConfig(ContainerAccess.Private)))));
        Assert.Equal(
            "1D 01 05 02 01 00 01 03 41 42 43",
            Kit.ToHex(Kit.Encode(new BlockConfigIntent(new EntityId(5), _cell, new TeleportNameConfig("ABC")))));
    }

    [Fact]
    public void TeleportNameOver32Bytes_Write_Throws()
    {
        var valid = new BlockConfigIntent(new EntityId(5), _cell, new TeleportNameConfig(new string('x', 32)));
        Assert.Equal(valid, Kit.DecodeIntent(Kit.Encode(valid)));

        // 33 bytes, and 17 two-byte letters; the limit counts UTF-8 bytes, not characters. An empty name is refused too.
        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { Config = new TeleportNameConfig(new string('x', 33)) }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { Config = new TeleportNameConfig(new string('ż', 17)) }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { Config = new TeleportNameConfig(string.Empty) }));
    }

    [Fact]
    public void Write_InvalidValues_Throws()
    {
        var interact = new BlockInteractIntent(new EntityId(5), _cell, BlockInteractAction.Toggle);
        var config = new BlockConfigIntent(new EntityId(5), _cell, new ContainerAccessConfig(ContainerAccess.Shared));
        Assert.Equal(interact, Kit.DecodeIntent(Kit.Encode(interact)));
        Assert.Equal(config, Kit.DecodeIntent(Kit.Encode(config)));

        Assert.Throws<ArgumentException>(() => Kit.Encode(interact with { Action = 0 }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(interact with { Action = (BlockInteractAction)4 }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(config with { Config = new ContainerAccessConfig(0) }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(config with { Config = new ContainerAccessConfig((ContainerAccess)3) }));
    }

    [Fact]
    public void UnknownAction_Read_Throws()
    {
        Assert.Equal(
            new BlockInteractIntent(new EntityId(5), _cell, BlockInteractAction.SetRespawn), Kit.DecodeIntent(Kit.Hex("1C 01 05 02 01 00 03")));

        Kit.AssertIntentViolation("1C 01 05 02 01 00 00");
        Kit.AssertIntentViolation("1C 01 05 02 01 00 04");
        Kit.AssertIntentViolation("1C 02 05 02 01 00 01");
    }

    [Fact]
    public void UnknownConfigVariant_Read_Throws()
    {
        Assert.Equal(
            new BlockConfigIntent(new EntityId(5), _cell, new ContainerAccessConfig(ContainerAccess.Private)),
            Kit.DecodeIntent(Kit.Hex("1D 01 05 02 01 00 02 02")));

        Kit.AssertIntentViolation("1D 01 05 02 01 00 00 02");
        Kit.AssertIntentViolation("1D 01 05 02 01 00 03 02");
        Kit.AssertIntentViolation("1D 01 05 02 01 00 02 00");
        Kit.AssertIntentViolation("1D 01 05 02 01 00 02 03");
    }

    [Fact]
    public void Read_InvalidTeleportName_Throws()
    {
        Assert.Equal(
            new BlockConfigIntent(new EntityId(5), _cell, new TeleportNameConfig("ABC")), Kit.DecodeIntent(Kit.Hex("1D 01 05 02 01 00 01 03 41 42 43")));

        // Empty, longer than the bytes that follow, 33 bytes and an invalid UTF-8 sequence.
        Kit.AssertIntentViolation("1D 01 05 02 01 00 01 00");
        Kit.AssertIntentViolation("1D 01 05 02 01 00 01 03 41 42");
        Kit.AssertIntentViolation("1D 01 05 02 01 00 01 21 " + string.Join(' ', Enumerable.Repeat("41", 33)));
        Kit.AssertIntentViolation("1D 01 05 02 01 00 01 02 C3 28");
    }

    [Fact]
    public void Read_VersionTrailingBytesAndNeighbourCodes_Throws()
    {
        Assert.Equal(new QuickStackIntent(), Kit.DecodeIntent(Kit.Hex("16 01")));
        Assert.Equal(new TakeAllIntent(new ContainerId(7)), Kit.DecodeIntent(Kit.Hex("17 01 07")));

        Kit.AssertIntentViolation("16 02");
        Kit.AssertIntentViolation("16 01 00");
        Kit.AssertIntentViolation("17 01");
        Kit.AssertIntentViolation("17 01 07 00");

        // Codes of the survival range that no package defines yet (1B is the prop hit of K6-31).
        foreach (var code in new[] { "18", "19", "1A", "1E" })
        {
            Kit.AssertIntentViolation(code + " 01");
        }
    }
}
