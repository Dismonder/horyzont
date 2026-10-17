using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Serialization;

public sealed class OperationCodecItemTests
{
    private static readonly SlotRef _a = new(new ContainerId(3), 7);
    private static readonly SlotRef _b = new(new ContainerId(300), 255);

    public static TheoryData<ItemSource> Sources => [.. Enum.GetValues<ItemSource>()];

    public static TheoryData<ItemSink> Sinks => [.. Enum.GetValues<ItemSink>()];

    public static TheoryData<ContainerKind> Kinds => [.. Enum.GetValues<ContainerKind>()];

    public static TheoryData<ContainerAccess> Accesses => [.. Enum.GetValues<ContainerAccess>()];

    public static TheoryData<int> Hosts => [0, 1, 2, 3];

    [Theory]
    [MemberData(nameof(Sources))]
    public void RoundTrip_ItemCreate_EverySource(ItemSource source) =>
        RoundTrip(new ItemCreateOp(_a, new ItemTypeId(4095), 499, 500, source));

    [Theory]
    [MemberData(nameof(Sinks))]
    public void RoundTrip_ItemDestroy_EverySink(ItemSink sink) =>
        RoundTrip(new ItemDestroyOp(_b, new ItemTypeId(1), ushort.MaxValue, sink));

    [Theory]
    [MemberData(nameof(Kinds))]
    public void RoundTrip_ContainerCreate_EveryKind(ContainerKind kind) =>
        RoundTrip(new ContainerCreateOp(new ContainerId(ulong.MaxValue), kind, ContainerHost.Player(new PlayerId(9)), new PlayerId(9), ContainerAccess.Private, 256));

    [Theory]
    [MemberData(nameof(Accesses))]
    public void RoundTrip_ContainerCreate_EveryAccess(ContainerAccess access) =>
        RoundTrip(new ContainerCreateOp(new ContainerId(1), ContainerKind.Chest, ContainerHost.Player(new PlayerId(1)), PlayerId.None, access, 1));

    [Theory]
    [MemberData(nameof(Hosts))]
    public void RoundTrip_ContainerCreate_EveryHostKind(int host) =>
        RoundTrip(new ContainerCreateOp(new ContainerId(5), ContainerKind.Chest, HostOf(host), new PlayerId(2), ContainerAccess.Shared, 30));

    [Fact]
    public void RoundTrip_TransferSwapAndCompound()
    {
        var transfer = new ItemTransferOp(_a, _b, new ItemTypeId(2), 17, 500);
        var swap = new ItemSwapOp(_a, _b, ItemTypeId.None, new ItemTypeId(3));
        var edit = new TerrainEditOp(new PlanetId(1), TerrainShape.Sphere, new Vector3(1, 4000, 2), 0.5f, TerrainEditMode.Remove, 0);

        RoundTrip(transfer);
        RoundTrip(swap);
        RoundTrip(new CompoundOp([transfer, swap, new ItemCreateOp(_a, new ItemTypeId(1), 1, 1, ItemSource.Starter), edit]));
    }

    [Fact]
    public void ItemTransfer_GoldenBytes()
    {
        var bytes = Kit.Encode(new ItemTransferOp(new SlotRef(new ContainerId(3), 7), new SlotRef(new ContainerId(300), 1), new ItemTypeId(2), 17, 500));

        Assert.Equal("1F 01 03 07 AC 02 01 02 11 F4 03", Kit.ToHex(bytes));
    }

    [Fact]
    public void Compound_GoldenBytes()
    {
        var bytes = Kit.Encode(new CompoundOp([new ItemDestroyOp(new SlotRef(new ContainerId(1), 2), new ItemTypeId(3), 4, ItemSink.BuildCost)]));

        Assert.Equal("1E 01 01 22 01 01 02 03 04 05", Kit.ToHex(bytes));
    }

    [Fact]
    public void Compound4097Components_Throws()
    {
        var components = Enumerable.Repeat<Operation>(new ItemSwapOp(_a, _b, ItemTypeId.None, new ItemTypeId(1)), ProtocolConstants.MaxCompoundComponents + 1).ToArray();

        Assert.Throws<ArgumentException>(() => Kit.Encode(new CompoundOp(components)));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new CompoundOp([])));
        Kit.AssertOperationViolation("1E 01 00");
        Kit.AssertOperationViolation("1E 01 81 20");

        var max = Kit.Encode(new CompoundOp(components[..ProtocolConstants.MaxCompoundComponents]));
        Assert.Equal(ProtocolConstants.MaxCompoundComponents, ((CompoundOp)Kit.DecodeOperation(max)).Components.Count);
    }

    [Fact]
    public void CompoundOver160KiB_Throws()
    {
        var big = new ContainerCreateOp(
            new ContainerId(ulong.MaxValue), ContainerKind.Chest, ContainerHost.Poi(new PlanetId(ulong.MaxValue), ulong.MaxValue, ulong.MaxValue),
            new PlayerId(ulong.MaxValue), ContainerAccess.Shared, 256);
        var components = Enumerable.Repeat<Operation>(big, ProtocolConstants.MaxCompoundComponents).ToArray();
        Assert.True(Kit.Encode(big).Length * components.Length > ProtocolConstants.MaxCompoundBytes);

        Assert.Throws<ArgumentException>(() => Kit.Encode(new CompoundOp(components)));

        var frame = new ByteWriter();
        frame.WriteByte((byte)OperationKind.Compound);
        frame.WriteByte(1);
        frame.WriteVarUInt((ulong)components.Length);
        foreach (var component in components)
        {
            OperationCodec.Write(frame, component);
        }

        Kit.AssertViolation(d => Kit.DecodeOperation(d), frame.WrittenSpan.ToArray());
    }

    [Fact]
    public void NestedCompound_Read_Throws()
    {
        Assert.Throws<ArgumentException>(() => Kit.Encode(new CompoundOp([new CompoundOp([new ItemSwapOp(_a, _b, ItemTypeId.None, new ItemTypeId(1))])])));

        Kit.AssertOperationViolation("1E 01 01 1E 01 01 20 01 03 07 03 07 00 01");
    }

    [Fact]
    public void InstanceByteNonZero_Throws()
    {
        var record = new ContainerRecord(
            new ContainerId(1), ContainerKind.Hotbar, ContainerHost.Player(new PlayerId(1)), new PlayerId(1), ContainerAccess.Private, 2, 1,
            [new ItemStack(new ItemTypeId(1), 1), ItemStack.Empty]);
        var blob = StateCodec.EncodeContainerBlob(record);
        Assert.Equal(0, blob[^1]);
        blob[^1] = 1;

        Kit.AssertViolation(d => StateCodec.DecodeContainerBlob(d), blob);
        Assert.Throws<ArgumentException>(() => StateCodec.EncodeContainerBlob(
            record with { Slots = [new ItemStack(new ItemTypeId(1), 1, new ItemInstanceData()), ItemStack.Empty] }));
    }

    [Fact]
    public void UnknownSourceSinkKindHost_Throws()
    {
        var player = ContainerHost.Player(new PlayerId(1));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new ItemCreateOp(_a, new ItemTypeId(1), 1, 1, (ItemSource)10)));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new ItemCreateOp(_a, new ItemTypeId(1), 1, 1, 0)));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new ItemDestroyOp(_a, new ItemTypeId(1), 1, (ItemSink)12)));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new ContainerCreateOp(new ContainerId(1), (ContainerKind)10, player, default, ContainerAccess.Shared, 1)));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new ContainerCreateOp(new ContainerId(1), ContainerKind.Chest, player, default, (ContainerAccess)3, 1)));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new ContainerCreateOp(
            new ContainerId(1), ContainerKind.Chest, player with { Kind = (ContainerHostKind)5 }, default, ContainerAccess.Shared, 1)));

        // ItemCreateOp: kind 33, version 1, slot (3, 7), type 1, count 1, limit 1, source.
        Kit.AssertOperationViolation("21 01 03 07 01 01 01 0A");
        Kit.AssertOperationViolation("21 01 03 07 01 01 01 00");
        // ItemDestroyOp: kind 34, slot, type, count, sink 12.
        Kit.AssertOperationViolation("22 01 03 07 01 01 0C");
        // ContainerCreateOp: kind 35, id 1, container kind 10 / host kind 5 / access 3.
        Kit.AssertOperationViolation("23 01 01 0A 01 01 01 01 01");
        Kit.AssertOperationViolation("23 01 01 04 05 01 01 01 01");
        Kit.AssertOperationViolation("23 01 01 04 01 01 01 03 01");
        Assert.IsType<ContainerCreateOp>(Kit.DecodeOperation(Kit.Hex("23 01 01 04 01 01 01 01 01")));
    }

    [Fact]
    public void InvalidSourceOrSink_Throws_BeforeWritingAnyByte()
    {
        var writer = new ByteWriter();

        Assert.Throws<ArgumentException>(() => OperationCodec.Write(writer, new ItemCreateOp(_a, new ItemTypeId(1), 1, 1, (ItemSource)10)));
        Assert.Throws<ArgumentException>(() => OperationCodec.Write(writer, new ItemDestroyOp(_a, new ItemTypeId(1), 1, (ItemSink)12)));

        Assert.Equal(0, writer.Length);
    }

    private static ContainerHost HostOf(int host) => host switch
    {
        0 => ContainerHost.Player(new PlayerId(12)),
        1 => ContainerHost.ConstructBlock(new EntityId(77), new CellPos(-3, 0, 511)),
        2 => ContainerHost.PersistentEntity(new EntityId(1_000_000)),
        _ => ContainerHost.Poi(new PlanetId(2), 40, 3),
    };

    private static void RoundTrip(Operation op)
    {
        var bytes = Kit.Encode(op);
        var decoded = Kit.DecodeOperation(bytes);
        DeepAssert.Equal(op, decoded);
        Assert.Equal(bytes, Kit.Encode(decoded));
    }
}
