using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Tests.Props;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// Message 45 (<see cref="PropStates"/>) and the new reject reason 53 in <see cref="IntentReply"/> (K6-31 D17), format 1. The message type and the
/// reason have no enum member yet (step B2), so the tests name them by number. Every refusal test first decodes the valid twin.
/// </summary>
public sealed class MessageCodecPropStatesTests
{
    // Region (1, 0) of face 0 holds these (cell u 9, v 2): slots 0 and 1 of the same cell and the cell u 10; the rock of face 1 is in another region.
    private static readonly PropId _tree = PropFixture.Raw(0, 9, 2, 0);
    private static readonly PropId _boulder = PropFixture.Raw(0, 9, 2, 1);
    private static readonly PropId _nextTree = PropFixture.Raw(0, 10, 2, 0);
    private static readonly PropId _rock = PropFixture.Raw(1, 0, 0, 1);

    private const string TreeBytes = "80 80 A0 81 80 40";
    private const string RockBytes = "80 80 84 80 80 80 80 80 04";
    private const string TreeRegionBytes = "80 80 08";
    private const string RockRegionBytes = "80 80 80 80 40";

    private static PropRegionState Region(PropId any, params (PropId Prop, ulong Tick)[] props) =>
        new(any.Region, [.. props.Select(p => new HarvestedPropEntry(p.Prop, new Tick(p.Tick)))]);

    public static TheoryData<Message> Messages() =>
    [
        new PropStates(new PlanetId(1), [Region(_tree)]),
        new PropStates(new PlanetId(1), [Region(_tree, (_tree, 300))]),
        new PropStates(
            new PlanetId(ulong.MaxValue),
            [Region(_tree, (_tree, 0), (_boulder, 1), (_nextTree, ulong.MaxValue)), Region(_rock), Region(PropId.Create(5, 100, 100, 0), (PropId.Create(5, 100, 100, 0), 7))]),
        new IntentSubmit(9, new PropHitIntent(new PlanetId(1), _tree, 4)),
    ];

    [Theory]
    [MemberData(nameof(Messages))]
    public void RoundTrip(Message message)
    {
        var frame = Kit.Encode(message);

        DeepAssert.Equal(message, MessageCodec.Read(frame));
        Assert.Equal((byte)message.Type, frame[0]);
        Assert.Equal(DeliveryChannel.ReliableOrdered, MessageChannels.For(message.Type));
    }

    [Fact]
    public void PropStates_Type_IsFortyFive()
    {
        var message = new PropStates(new PlanetId(1), [Region(_tree)]);

        Assert.Equal(PropProtocol.PropStatesType, message.Type);
        Assert.Equal(DeliveryChannel.ReliableOrdered, MessageChannels.For(PropProtocol.PropStatesType));
        Assert.Equal(45, Kit.Encode(message)[0]);
    }

    [Fact]
    public void GoldenBytes()
    {
        Assert.Equal("2D 01 01 01 " + TreeRegionBytes + " 00", Kit.ToHex(Kit.Encode(new PropStates(new PlanetId(1), [Region(_tree)]))));
        Assert.Equal(
            "2D 01 01 01 " + TreeRegionBytes + " 01 " + TreeBytes + " AC 02",
            Kit.ToHex(Kit.Encode(new PropStates(new PlanetId(1), [Region(_tree, (_tree, 300))]))));
        Assert.Equal(
            "2D 01 07 02 " + TreeRegionBytes + " 00 " + RockRegionBytes + " 01 " + RockBytes + " 00",
            Kit.ToHex(Kit.Encode(new PropStates(new PlanetId(7), [Region(_tree), Region(_rock, (_rock, 0))]))));
    }

    [Fact]
    public void Write_Over64RegionsOr4096Entries_Throws()
    {
        static PropStates Regions(int count) => new(
            new PlanetId(1), [.. Enumerable.Range(0, count).Select(i => new PropRegionState(new PropRegionKey((ulong)(i + 1)), []))]);

        var full = Regions(ProtocolConstants.MaxPropRegionsPerMessage);
        DeepAssert.Equal(full, MessageCodec.Read(Kit.Encode(full)));

        Assert.Throws<ArgumentException>(() => Kit.Encode(Regions(ProtocolConstants.MaxPropRegionsPerMessage + 1)));
        Assert.Throws<ArgumentException>(() => Kit.Encode(Regions(0)));

        // 4096 entries in 16 full regions are the most; one more in a 17th region is too many (a region holds at most 256 ids).
        var most = new PropStates(new PlanetId(1), [.. Enumerable.Range(0, 16).Select(r => FullRegion(r, 256))]);
        DeepAssert.Equal(most, MessageCodec.Read(Kit.Encode(most)));
        Assert.True(Kit.Encode(most).Length < ProtocolConstants.MaxReliableMessageBytes);

        Assert.Throws<ArgumentException>(() => Kit.Encode(new PropStates(new PlanetId(1), [.. Enumerable.Range(0, 16).Select(r => FullRegion(r, 256)), FullRegion(16, 1)])));
    }

    /// <summary>Region (r, 0) of face 0 with its first <paramref name="count"/> ids in ascending order (cell u, cell v, slot).</summary>
    private static PropRegionState FullRegion(int r, int count)
    {
        var entries = new List<HarvestedPropEntry>();
        for (var cellU = 0; cellU < 8; cellU++)
        {
            for (var cellV = 0; cellV < 8; cellV++)
            {
                for (var slot = 0; slot < 4; slot++)
                {
                    if (entries.Count < count)
                    {
                        entries.Add(new HarvestedPropEntry(PropFixture.Raw(0, (r * 8) + cellU, cellV, slot), new Tick(1)));
                    }
                }
            }
        }

        return new PropRegionState(PropFixture.Raw(0, r * 8, 0, 0).Region, entries);
    }

    [Fact]
    public void Read_Over64Regions_Throws()
    {
        DeepAssert.Equal(new PropStates(new PlanetId(1), [Region(_tree)]), MessageCodec.Read(Kit.Hex("2D 01 01 01 " + TreeRegionBytes + " 00")));

        // 65 regions announced: refused before anything is read after the count.
        Kit.AssertMessageViolation("2D 01 01 41");
        Kit.AssertMessageViolation("2D 01 01 00");
        Kit.AssertMessageViolation("2D 01 01 FF FF 03");
    }

    [Fact]
    public void Over4096Entries_ThrowsBeforeAllocation()
    {
        DeepAssert.Equal(
            new PropStates(new PlanetId(1), [Region(_tree, (_tree, 300))]), MessageCodec.Read(Kit.Hex("2D 01 01 01 " + TreeRegionBytes + " 01 " + TreeBytes + " AC 02")));

        // 4097 entries announced in a region and a count of 2^30: the frame has no bytes for them, so the read must refuse on the count and
        // not try to allocate a list for it.
        Kit.AssertMessageViolation("2D 01 01 01 " + TreeRegionBytes + " 81 20");
        Kit.AssertMessageViolation("2D 01 01 01 " + TreeRegionBytes + " 80 80 80 80 04");
    }

    [Fact]
    public void InvalidRegionKey_Throws()
    {
        DeepAssert.Equal(new PropStates(new PlanetId(1), [Region(_rock)]), MessageCodec.Read(Kit.Hex("2D 01 01 01 " + RockRegionBytes + " 00")));

        // A key is 37 bits (face 0..5, 17 bits of region u, 17 of region v): bit 37 set, face 6 and face 7 are refused on read and on write.
        Kit.AssertMessageViolation("2D 01 01 01 80 80 80 80 80 04 00");
        Kit.AssertMessageViolation("2D 01 01 01 80 80 80 80 80 03 00");
        Kit.AssertMessageViolation("2D 01 01 01 80 80 80 80 C0 03 00");
        foreach (var key in new[] { 1UL << 37, 6UL << 34, 7UL << 34 })
        {
            Assert.Throws<ArgumentException>(() => Kit.Encode(new PropStates(new PlanetId(1), [new PropRegionState(new PropRegionKey(key), [])])));
        }
    }

    [Fact]
    public void Read_RegionOver256Entries_ThrowsBeforeAllocation()
    {
        DeepAssert.Equal(
            new PropStates(new PlanetId(1), [Region(_tree, (_tree, 300))]), MessageCodec.Read(Kit.Hex("2D 01 01 01 " + TreeRegionBytes + " 01 " + TreeBytes + " AC 02")));

        // A region holds at most 256 ids (8 x 8 cells, 4 slots), so 257 entries cannot be a valid region: refused on the count, before a list
        // is allocated for it (the frame has no bytes for the entries).
        var full = new PropStates(new PlanetId(1), [FullRegion(0, 256)]);
        DeepAssert.Equal(full, MessageCodec.Read(Kit.Encode(full)));
        Kit.AssertMessageViolation("2D 01 01 01 80 80 80 80 80 00 81 02");
        Kit.AssertMessageViolation("2D 01 01 01 " + TreeRegionBytes + " 81 02");
        Assert.Throws<ArgumentException>(() => Kit.Encode(new PropStates(
            new PlanetId(1), [new PropRegionState(_tree.Region, [.. Enumerable.Repeat(new HarvestedPropEntry(_tree, new Tick(1)), 257)])])));
    }

    [Fact]
    public void EntryOutsideRegion_Throws()
    {
        DeepAssert.Equal(
            new PropStates(new PlanetId(1), [Region(_tree, (_tree, 300))]), MessageCodec.Read(Kit.Hex("2D 01 01 01 " + TreeRegionBytes + " 01 " + TreeBytes + " AC 02")));

        // The rock of face 1 listed under the region of the tree.
        Kit.AssertMessageViolation("2D 01 01 01 " + TreeRegionBytes + " 01 " + RockBytes + " 00");
        Assert.Throws<ArgumentException>(() => Kit.Encode(new PropStates(new PlanetId(1), [new PropRegionState(_tree.Region, [new HarvestedPropEntry(_rock, new Tick(0))])])));
    }

    [Fact]
    public void UnsortedEntries_Throws()
    {
        DeepAssert.Equal(
            new PropStates(new PlanetId(1), [Region(_tree, (_tree, 1), (_boulder, 2))]),
            MessageCodec.Read(Kit.Hex("2D 01 01 01 " + TreeRegionBytes + " 02 " + TreeBytes + " 01 80 80 A4 81 80 40 02")));

        // Descending order and a repeated entry.
        Kit.AssertMessageViolation("2D 01 01 01 " + TreeRegionBytes + " 02 80 80 A4 81 80 40 02 " + TreeBytes + " 01");
        Kit.AssertMessageViolation("2D 01 01 01 " + TreeRegionBytes + " 02 " + TreeBytes + " 01 " + TreeBytes + " 02");
        Assert.Throws<ArgumentException>(() => Kit.Encode(new PropStates(new PlanetId(1), [Region(_tree, (_boulder, 2), (_tree, 1))])));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new PropStates(new PlanetId(1), [Region(_tree, (_tree, 1), (_tree, 2))])));
    }

    [Fact]
    public void DuplicateRegion_Throws()
    {
        DeepAssert.Equal(
            new PropStates(new PlanetId(1), [Region(_tree), Region(_rock)]),
            MessageCodec.Read(Kit.Hex("2D 01 01 02 " + TreeRegionBytes + " 00 " + RockRegionBytes + " 00")));

        // The same region twice, and the regions in descending order.
        Kit.AssertMessageViolation("2D 01 01 02 " + TreeRegionBytes + " 00 " + TreeRegionBytes + " 00");
        Kit.AssertMessageViolation("2D 01 01 02 " + RockRegionBytes + " 00 " + TreeRegionBytes + " 00");
        Assert.Throws<ArgumentException>(() => Kit.Encode(new PropStates(new PlanetId(1), [Region(_tree), Region(_tree)])));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new PropStates(new PlanetId(1), [Region(_rock), Region(_tree)])));
    }

    [Fact]
    public void Read_VersionAndTrailingBytes_Throws()
    {
        DeepAssert.Equal(new PropStates(new PlanetId(1), [Region(_tree)]), MessageCodec.Read(Kit.Hex("2D 01 01 01 " + TreeRegionBytes + " 00")));

        Kit.AssertMessageViolation("2D 02 01 01 " + TreeRegionBytes + " 00");
        Kit.AssertMessageViolation("2D 01 01 01 " + TreeRegionBytes + " 00 00");
        Kit.AssertMessageViolation("2D 01 01 01 " + TreeRegionBytes);
    }

    [Fact]
    public void IntentReply_ToolTierTooLow_RoundTrips()
    {
        var reply = new IntentReply(3, PropProtocol.ToolTierTooLow, OpId.None);

        Assert.Equal(reply, MessageCodec.Read(Kit.Encode(reply)));
        Assert.Equal("0C 01 03 35 00", Kit.ToHex(Kit.Encode(reply)));

        // Neighbouring codes of the survival range that no package defines.
        Kit.AssertMessageViolation("0C 01 03 34 00");
        Kit.AssertMessageViolation("0C 01 03 36 00");
    }
}
