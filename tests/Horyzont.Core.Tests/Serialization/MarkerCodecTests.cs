using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Markers;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>K5-27 formats: operations 22/23, intents 14/15, blob 11, message 33, reject reason 36. Bytes are derived by hand below.</summary>
public sealed class MarkerCodecTests
{
    // Marker body: id varuint 7, hasOwner 00, planet varuint 9, (1.0, -2.0, 0.5) as 3 little-endian floats,
    // name length 3 + "Dom", icon Base = 1, colour Green = 2.
    private const string SharedBody = "07 00 09  00 00 80 3F  00 00 00 C0  00 00 00 3F  03 44 6F 6D  01 02";

    // The same with hasOwner 01 and owner varuint 3.
    private const string PrivateBody = "07 01 03 09  00 00 80 3F  00 00 00 C0  00 00 00 3F  03 44 6F 6D  01 02";

    private static readonly MarkerRecord _shared = new(
        new MarkerId(7), null, new PlanetId(9), new Vector3(1f, -2f, 0.5f), "Dom", MarkerIcon.Base, MarkerColor.Green);

    private static readonly MarkerRecord _private = _shared with { Owner = new PlayerId(3) };

    [Fact]
    public void MarkerCodecs_GoldenBytes()
    {
        // MarkerSet = 22 (0x16), version 1, body.
        AssertOp(new MarkerSetOp(_shared.Id, null, _shared.Planet, _shared.Position, "Dom", _shared.Icon, _shared.Color), "16 01 " + SharedBody);
        AssertOp(new MarkerSetOp(_private.Id, _private.Owner, _private.Planet, _private.Position, "Dom", _private.Icon, _private.Color), "16 01 " + PrivateBody);

        // MarkerRemove = 23 (0x17), version 1, id varuint 7.
        AssertOp(new MarkerRemoveOp(new MarkerId(7)), "17 01 07");

        // Intent code 14 (0x0E), version 1, marker 0 (new), shared 01, planet 9, position, name, icon, colour.
        AssertIntent(
            new MarkerSetIntent(MarkerId.None, true, new PlanetId(9), _shared.Position, "Dom", MarkerIcon.Base, MarkerColor.Green),
            "0E 01 00 01 09  00 00 80 3F  00 00 00 C0  00 00 00 3F  03 44 6F 6D  01 02");

        // Intent code 15 (0x0F), version 1, marker varuint 7.
        AssertIntent(new MarkerRemoveIntent(new MarkerId(7)), "0F 01 07");

        // Blob kind 11 (0x0B), format 1, body.
        Assert.Equal(Kit.Hex("0B 01 " + PrivateBody), StateCodec.EncodeMarkerBlob(_private));
        Assert.Equal(_private, StateCodec.DecodeMarkerBlob(Kit.Hex("0B 01 " + PrivateBody)));

        // MarkersState = 33 (0x21), version 1, count varuint 2, two bodies.
        var message = new MarkersState([_shared, _private]);
        Assert.Equal(Kit.Hex("21 01 02 " + SharedBody + " " + PrivateBody), Kit.Encode(message));
        Assert.Equal(message.Markers, ((MarkersState)MessageCodec.Read(Kit.Hex("21 01 02 " + SharedBody + " " + PrivateBody))).Markers);

        // IntentReply carries RejectReason.MarkerLimit = 36 (0x24).
        Assert.Equal(36, (byte)RejectReason.MarkerLimit);
        Assert.Equal(22, (byte)OperationKind.MarkerSet);
        Assert.Equal(23, (byte)OperationKind.MarkerRemove);
        Assert.Equal(11, (byte)BlobKind.Marker);
        Assert.Equal(33, (byte)MessageType.MarkersState);
    }

    [Fact]
    public void MarkerCodecs_DamagedInput_Rejected()
    {
        // Icon 8, colour 8, owner 0, id 0 and a name over 32 bytes are protocol violations.
        foreach (var bad in new[]
        {
            "16 01 07 00 09  00 00 80 3F 00 00 00 C0 00 00 00 3F  03 44 6F 6D  08 02",
            "16 01 07 00 09  00 00 80 3F 00 00 00 C0 00 00 00 3F  03 44 6F 6D  01 08",
            "16 01 07 01 00 09  00 00 80 3F 00 00 00 C0 00 00 00 3F  03 44 6F 6D  01 02",
            "16 01 00 00 09  00 00 80 3F 00 00 00 C0 00 00 00 3F  03 44 6F 6D  01 02",
            "16 01 07 00 09  00 00 80 3F 00 00 00 C0 00 00 00 3F  21 " + string.Concat(Enumerable.Repeat("61 ", 33)) + " 01 02",
        })
        {
            Assert.Throws<ProtocolViolationException>(() => Kit.DecodeOperation(Kit.Hex(bad)));
        }

        Assert.Throws<ProtocolViolationException>(() => Kit.DecodeIntent(Kit.Hex("0E 01 00 01 09  00 00 80 3F 00 00 00 C0 00 00 00 3F  03 44 6F 6D  09 02")));
    }

    [Fact]
    public void MarkerRemoveCodecs_IdZeroOrOverFourBillion_Rejected()
    {
        // 80 80 80 80 10 is 2^32, one past the largest id.
        foreach (var bad in new[] { "17 01 00", "17 01 80 80 80 80 10" })
        {
            Assert.Throws<ProtocolViolationException>(() => Kit.DecodeOperation(Kit.Hex(bad)));
        }

        foreach (var bad in new[] { "0F 01 00", "0F 01 80 80 80 80 10" })
        {
            Assert.Throws<ProtocolViolationException>(() => Kit.DecodeIntent(Kit.Hex(bad)));
        }

        Assert.IsType<MarkerRemoveOp>(Kit.DecodeOperation(Kit.Hex("17 01 05")));
        Assert.IsType<MarkerRemoveIntent>(Kit.DecodeIntent(Kit.Hex("0F 01 05")));
    }

    [Fact]
    public void MessageCodec_MarkersState_MaxFitsAndOverCountRejected()
    {
        var writer = new ByteWriter();
        MessageCodec.Write(writer, new MarkersState(MaxMessages.MaxMarkers()));
        var frame = writer.WrittenSpan.ToArray();

        Assert.InRange(frame.Length, 0, 40_000);
        Assert.True(frame.Length < ProtocolConstants.MaxReliableMessageBytes);
        Assert.Equal(DeliveryChannel.ReliableOrdered, MessageChannels.For(MessageType.MarkersState));
        Assert.Equal(512, ((MarkersState)MessageCodec.Read(frame)).Markers.Count);
        Assert.Throws<ArgumentException>(() => MessageCodec.Write(new ByteWriter(), new MarkersState([.. MaxMessages.MaxMarkers(), _shared])));
        Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read(Kit.Hex("21 01 80 04")));
    }

    private static void AssertOp(Operation op, string hex)
    {
        Assert.Equal(Kit.Hex(hex), Kit.Encode(op));
        Assert.Equal(op, Kit.DecodeOperation(Kit.Hex(hex)));
    }

    private static void AssertIntent(Intent intent, string hex)
    {
        Assert.Equal(Kit.Hex(hex), Kit.Encode(intent));
        Assert.Equal(intent, Kit.DecodeIntent(Kit.Hex(hex)));
    }
}
