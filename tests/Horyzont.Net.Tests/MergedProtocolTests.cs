using System.Numerics;
using System.Runtime.CompilerServices;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.Space;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Markers;
using Horyzont.Net.Tests.Session;
using Horyzont.Net.Transport;

namespace Horyzont.Net.Tests;

public sealed class MergedProtocolTests
{
    [Theory]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    public void Handshake_MergedProtocolVersion_AcceptsOnlyEighteen(int version)
    {
        Assert.Equal(18, ProtocolConstants.ProtocolVersion);
        using var h = new SessionHarness();
        var raw = h.AddRaw();
        Assert.True(h.RunUntil(() => raw.IsConnected, TimeSpan.FromSeconds(1)));
        raw.Send(TransportChannel.ReliableOrdered, SessionHarness.Encode(new ClientHello(
            (ushort)version, SessionHarness.Build, SessionHarness.DevIdentity(5), "player", "", [])));

        Assert.True(h.RunUntil(() => raw.Messages().Any(), TimeSpan.FromSeconds(2)));
        var response = Assert.Single(raw.Messages());
        if (version == 18)
        {
            Assert.IsType<ServerHello>(response);
            Assert.Single(h.ServerHooks.Joined);
        }
        else
        {
            Assert.Equal(HandshakeRejectCode.WrongProtocol, Assert.IsType<HandshakeReject>(response).Code);
            Assert.Empty(h.ServerHooks.Calls);
        }
    }

    [Fact]
    public void MessageTypes_AllConcreteMessages_HaveUniqueIdsAndOnlyDocumentedGaps()
    {
        var enumIds = Enum.GetValues<MessageType>().Select(value => (int)value).ToArray();
        Assert.Equal(enumIds.Length, enumIds.Distinct().Count());
        // MessageType ranges: 26..29 remain in K2-K4; Space 30 and 34..39 are unimplemented
        // (spec 02 reserves 30/34 for seats). Survival 47 belongs to a later package (spec 03); 44, 45 and 48 are taken by K6-33, K6-31 and K6-32.
        int[] reserved = [26, 27, 28, 29, 30, 34, 35, 36, 37, 38, 39, 47];
        Assert.Equal(Enumerable.Range(1, 49).Except(reserved), enumIds);
        var messages = typeof(Message).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(Message).IsAssignableFrom(type))
            .Select(type => (Message)RuntimeHelpers.GetUninitializedObject(type)).ToArray();
        Assert.Equal(enumIds.Length, messages.Length);
        Assert.Equal(enumIds, messages.Select(message => (int)message.Type).Order());
        foreach (var message in messages)
        {
            Assert.Equal(message.GetType().Name, message.Type.ToString());
        }
        Assert.Equal(31, (int)MessageType.ShipStatus);
        Assert.Equal(32, (int)MessageType.StarSystemState);
        Assert.Equal(33, (int)MessageType.MarkersState);
        Assert.Equal(40, (int)MessageType.ContainerState);
        Assert.Equal(45, (int)MessageType.PropStates);
        Assert.Equal(46, (int)MessageType.BlockEntityStates);
        Assert.Equal(48, (int)MessageType.PlayerProgressState);
        Assert.Equal(49, (int)MessageType.ItemRegistry);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void MessageCodec_MergedMessages_RestoresEveryField(int scenario)
    {
        Message original = scenario switch
        {
            0 => new ContainerState(new ContainerId(17), RejectReason.None, ContainerKind.PlayerBag,
                ContainerAccess.Private, 40, 7, [new SlotContent(0, new ItemStack(new ItemTypeId(3), 12)),
                    new SlotContent(39, new ItemStack(new ItemTypeId(4096), 1))]),
            1 => ContainerState.Rejected(new ContainerId(18), RejectReason.ContainerLocked),
            2 => new ItemRegistry([new RegistryEntry(1, "core:stone"), new RegistryEntry(4096, "core:tool")]),
            3 => new ShipStatus(new EntityId(91), new Tick(73), 12.5f, 100.25f, 321.5f,
                (ShipFlightFlags)3, ShipLaunchBlock.None),
            4 => new StarSystemState(StarSystemGenesis.CreateWorldBodies(42, "core:planet/1").System),
            5 => new MarkersState([new MarkerRecord(new MarkerId(1), null, new PlanetId(2),
                    new Vector3(1.25f, -2.5f, 3.75f), "Shared", MarkerIcon.Base, MarkerColor.Blue),
                new MarkerRecord(new MarkerId(2), new PlayerId(9), new PlanetId(3),
                    new Vector3(-4, 5, 6), "Private", MarkerIcon.Resource, MarkerColor.Red)]),
            6 => new ContainerDelta(new ContainerId(17), 8,
                [new SlotContent(0, ItemStack.Empty), new SlotContent(39, new ItemStack(new ItemTypeId(3), 2))]),
            7 => new MarkersState([]),
            8 => new ContainerSubscribe(new ContainerId(17)),
            9 => new ContainerUnsubscribe(new ContainerId(17)),
            10 => new BlockEntityStates(new EntityId(12), [new BlockEntityEntry(new CellPos(-3, 0, 5), new ChestBlockState(new ContainerId(17))),
                new BlockEntityEntry(new CellPos(2, 1, 0), new BedBlockState(new PlayerId(4)))]),
            11 => new PropStates(new PlanetId(1), [
                new PropRegionState(PropId.Create(0, 9, 2, 0).Region, [new HarvestedPropEntry(PropId.Create(0, 9, 2, 0), new Tick(300))]),
                new PropRegionState(PropId.Create(1, 0, 0, 1).Region, [])]),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        var bytes = SessionHarness.Encode(original);
        var decoded = MessageCodec.Read(bytes);
        Assert.Equal(original.GetType(), decoded.GetType());
        Assert.Equal(original.Type, decoded.Type);
        switch (original)
        {
            case ContainerState expected:
                var state = Assert.IsType<ContainerState>(decoded);
                Assert.Equal(expected with { Slots = state.Slots }, state);
                Assert.Equal(expected.Slots, state.Slots);
                break;
            case ItemRegistry expected:
                Assert.Equal(expected.Entries, Assert.IsType<ItemRegistry>(decoded).Entries);
                break;
            case StarSystemState expected:
                var system = Assert.IsType<StarSystemState>(decoded);
                Assert.Equal(expected.System, system.System);
                break;
            case MarkersState expected:
                Assert.Equal(expected.Markers, Assert.IsType<MarkersState>(decoded).Markers);
                break;
            case BlockEntityStates expected:
                var states = Assert.IsType<BlockEntityStates>(decoded);
                Assert.Equal(expected.Construct, states.Construct);
                Assert.Equal(expected.Entries, states.Entries);
                break;
            case PropStates expected:
                var props = Assert.IsType<PropStates>(decoded);
                Assert.Equal(expected.Planet, props.Planet);
                Assert.Equal(expected.Regions.Select(r => r.Region), props.Regions.Select(r => r.Region));
                Assert.Equal(expected.Regions.SelectMany(r => r.Props), props.Regions.SelectMany(r => r.Props));
                break;
            case ContainerDelta expected:
                var delta = Assert.IsType<ContainerDelta>(decoded);
                Assert.Equal(expected with { Changed = delta.Changed }, delta);
                Assert.Equal(expected.Changed, delta.Changed);
                break;
            default:
                Assert.Equal(original, decoded);
                break;
        }
        Assert.Equal(bytes, SessionHarness.Encode(decoded));
    }
}
