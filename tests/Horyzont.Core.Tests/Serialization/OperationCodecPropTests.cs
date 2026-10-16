using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.Props;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>K6-31, format 1 of the operations 47 and 48 and of the compound with them.</summary>
public sealed class OperationCodecPropTests
{
    private static readonly PlanetId _planet = new(1);

    // Face 1, cell 0,0, slot 1: bits 58 and 16 -> LEB128 80 80 84 80 80 80 80 80 04.
    private static readonly PropId _rock = PropFixture.Raw(1, 0, 0, 1);

    // Face 0, cell u 9 (region 1, cell 1), v 2 (region 0, cell 2), slot 0: bits 41, 21 and 19 -> LEB128 80 80 A0 81 80 40.
    private static readonly PropId _tree = PropFixture.Raw(0, 9, 2, 0);

    [Fact]
    public void RoundTrip_PropHarvest()
    {
        RoundTrip(new PropHarvestOp(_planet, _tree, new Tick(0)));
        RoundTrip(new PropHarvestOp(new PlanetId(ulong.MaxValue), _rock, new Tick(ulong.MaxValue)));
        RoundTrip(new PropHarvestOp(_planet, PropId.Create(5, PropId.MaxCellsPerFace - 1, PropId.MaxCellsPerFace - 1, 3), new Tick(123_456_789)));
    }

    [Fact]
    public void RoundTrip_PropRegrow()
    {
        RoundTrip(new PropRegrowOp(_planet, _tree));
        RoundTrip(new PropRegrowOp(new PlanetId(7), _rock));
    }

    [Fact]
    public void RoundTrip_CompoundWithPropComponents()
    {
        var item = new ItemCreateOp(new SlotRef(new ContainerId(1), 0), new ItemTypeId(3), 4, 500, ItemSource.Harvest);

        RoundTrip(new CompoundOp([new PropHarvestOp(_planet, _tree, new Tick(9)), item]));
        RoundTrip(new CompoundOp([new PropRegrowOp(_planet, _tree), new PropRegrowOp(_planet, _rock)]));
        RoundTrip(new CompoundOp(
        [
            item,
            new PropHarvestOp(_planet, _tree, new Tick(9)),
            new TerrainEditOp(_planet, TerrainShape.Sphere, new System.Numerics.Vector3(1, 2, 3), 0.5f, TerrainEditMode.Remove, 0),
        ]));
    }

    [Fact]
    public void GoldenBytes_PropOperations()
    {
        // Kind 48 = 0x30 and 47 = 0x2F, format 1, planet, prop, tick (LEB128, 300 = AC 02).
        Assert.Equal("30 01 01 80 80 84 80 80 80 80 80 04 AC 02", Kit.ToHex(Kit.Encode(new PropHarvestOp(_planet, _rock, new Tick(300)))));
        Assert.Equal("30 01 01 80 80 A0 81 80 40 00", Kit.ToHex(Kit.Encode(new PropHarvestOp(_planet, _tree, new Tick(0)))));
        Assert.Equal("2F 01 01 80 80 A0 81 80 40", Kit.ToHex(Kit.Encode(new PropRegrowOp(_planet, _tree))));
        Assert.Equal("2F 01 07 80 80 84 80 80 80 80 80 04", Kit.ToHex(Kit.Encode(new PropRegrowOp(new PlanetId(7), _rock))));
        Assert.Equal(new PropHarvestOp(_planet, _rock, new Tick(300)), Kit.DecodeOperation(Kit.Hex("30 01 01 80 80 84 80 80 80 80 80 04 AC 02")));
        Assert.Equal(new PropRegrowOp(_planet, _tree), Kit.DecodeOperation(Kit.Hex("2F 01 01 80 80 A0 81 80 40")));
    }

    [Fact]
    public void ReservedBitsPropId_Read_Throws()
    {
        var valid = Kit.Hex("30 01 01 80 80 84 80 80 80 80 80 04 AC 02");
        Assert.IsType<PropHarvestOp>(Kit.DecodeOperation(valid));

        // Bit 61 (reserved) set: the last LEB128 group (bits 56-62) becomes 04 | 20 = 24; bit 16 -> bit 0 (low reserved bits): the first byte 80 -> 81.
        var highReserved = (byte[])valid.Clone();
        highReserved[11] = 0x24;
        var lowReserved = (byte[])valid.Clone();
        lowReserved[3] = 0x81;
        // Face 6 (bits 60 and 59): the last group is 10 | 08 = 18.
        var face6 = (byte[])valid.Clone();
        face6[11] = 0x18;

        Kit.AssertViolation(d => Kit.DecodeOperation(d), highReserved);
        Kit.AssertViolation(d => Kit.DecodeOperation(d), lowReserved);
        Kit.AssertViolation(d => Kit.DecodeOperation(d), face6);
        Kit.AssertViolation(d => Kit.DecodeOperation(d), Kit.Hex("2F 01 01 80 80 84 80 80 80 80 80 24"));
    }

    [Fact]
    public void ReservedBitsPropId_Write_Throws()
    {
        RoundTrip(new PropHarvestOp(_planet, _rock, new Tick(5)));
        RoundTrip(new PropRegrowOp(_planet, _rock));

        var low = new PropId(_rock.Value | 1UL);
        var high = new PropId(_rock.Value | (1UL << 62));
        var face6 = new PropId(6UL << 58);

        Assert.Throws<ArgumentException>(() => Kit.Encode(new PropHarvestOp(_planet, low, new Tick(5))));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new PropHarvestOp(_planet, high, new Tick(5))));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new PropHarvestOp(_planet, face6, new Tick(5))));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new PropRegrowOp(_planet, low)));
    }

    private static void RoundTrip(Operation op)
    {
        var bytes = Kit.Encode(op);
        var decoded = Kit.DecodeOperation(bytes);
        DeepAssert.Equal(op, decoded);
        Assert.Equal(bytes, Kit.Encode(decoded));
    }
}
