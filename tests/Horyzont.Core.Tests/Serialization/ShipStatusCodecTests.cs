using System.Buffers.Binary;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>K5-26 decision 8: <c>ShipStatus</c> (31), 30 bytes after the type and format version, unreliable.</summary>
public sealed class ShipStatusCodecTests
{
    private static readonly ShipStatus _status = new(
        new EntityId(0x0102030405060708), new Tick(0x1112131415161718), 12.5f, 100f, 250f,
        ShipFlightFlags.Assist | ShipFlightFlags.GearDown, ShipLaunchBlock.FuelReserve);

    private static byte[] Encode(ShipStatus status)
    {
        var writer = new ByteWriter();
        MessageCodec.Write(writer, status);
        return writer.WrittenSpan.ToArray();
    }

    [Fact]
    public void MessageCodec_ShipStatus_GoldenBytes()
    {
        byte[] expected =
        [
            31, 1,
            0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
            0x18, 0x17, 0x16, 0x15, 0x14, 0x13, 0x12, 0x11,
            0x00, 0x00, 0x48, 0x41, // 12.5f
            0x00, 0x00, 0xC8, 0x42, // 100f
            0x00, 0x00, 0x7A, 0x43, // 250f
            0x05,
            0x01,
        ];
        Assert.Equal(expected, Encode(_status));
        Assert.Equal(32, expected.Length);
        var direct = new byte[MessageCodec.ShipStatusFrameBytes];
        var status = new ShipFlightStatus(_status.Fuel, _status.FuelCapacity, _status.SpeedLimit, _status.Flags, _status.LaunchBlock);
        Assert.Equal(32, MessageCodec.EncodeShipStatus(direct, _status.Ship, _status.Tick, in status));
        Assert.Equal(expected, direct);
        Assert.Throws<ArgumentException>(() => MessageCodec.EncodeShipStatus(new byte[31], _status.Ship, _status.Tick, in status));
        var negative = status with { Fuel = -1f };
        Assert.Throws<ArgumentException>(() => MessageCodec.EncodeShipStatus(direct, _status.Ship, _status.Tick, in negative));
        Assert.Equal(DeliveryChannel.Unreliable, MessageChannels.For(MessageType.ShipStatus));
    }

    [Fact]
    public void MessageCodec_ShipStatus_RoundTripAndRejects()
    {
        Assert.Equal(_status, MessageCodec.Read(Encode(_status)));
        var all = _status with { Flags = (ShipFlightFlags)0xFF, LaunchBlock = ShipLaunchBlock.None, Fuel = 0f };
        Assert.Equal(all, MessageCodec.Read(Encode(all)));

        // The writer refuses what the reader would refuse.
        Assert.Throws<ArgumentException>(() => Encode(_status with { Fuel = float.NaN }));
        Assert.Throws<ArgumentException>(() => Encode(_status with { SpeedLimit = float.PositiveInfinity }));
        Assert.Throws<ArgumentException>(() => Encode(_status with { FuelCapacity = -1f }));
        Assert.Throws<ArgumentException>(() => Encode(_status with { LaunchBlock = (ShipLaunchBlock)200 }));
        Assert.Throws<ArgumentException>(() => Encode(_status with { Ship = default }));

        var bytes = Encode(_status);
        foreach (var offset in new[] { 18, 22, 26 })
        {
            Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read(WithSingle(bytes, offset, float.NaN)));
            Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read(WithSingle(bytes, offset, float.NegativeInfinity)));
            Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read(WithSingle(bytes, offset, -0.5f)));
        }

        var block = (byte[])bytes.Clone();
        block[31] = 200;
        Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read(block));
        var noShip = (byte[])bytes.Clone();
        Array.Clear(noShip, 2, 8);
        Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read(noShip));
        Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read(bytes.AsSpan(0, 31).ToArray()));
        Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read([.. bytes, 0]));
    }

    private static byte[] WithSingle(byte[] bytes, int offset, float value)
    {
        var copy = (byte[])bytes.Clone();
        BinaryPrimitives.WriteSingleLittleEndian(copy.AsSpan(offset), value);
        return copy;
    }
}
