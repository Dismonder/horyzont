using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>How the lossy fields of messages (movement axes, velocity, compressed rotation) appear on the wire.</summary>
public sealed class QuantizedFieldTests
{
    [Fact]
    public void InputBatch_EncodesAxesAsSignedBytesAndButtonsAsTwoFixedBytes()
    {
        var batch = new InputBatch(1, [new PlayerInput(2, -1f, 1f, 1f, -2f, InputButtons.Jump | InputButtons.Primary)]);

        // 0x0D, version 1, FirstSeq 1, count 1, ClientTick 2, MoveX -127 = 0x81, MoveY 127 = 0x7F, Yaw 1.0, Pitch -2.0, Buttons 0x0009 little-endian.
        var expected = Kit.Hex("0D 01  01  01  02  81  7F  00 00 80 3F  00 00 00 C0  09 00");
        Assert.Equal(expected, Kit.Encode(batch));
        DeepAssert.Equal(batch, MessageCodec.Read(expected));
    }

    [Theory]
    [InlineData(0.3f)]
    [InlineData(-0.77f)]
    [InlineData(0.004f)]
    [InlineData(1f)]
    [InlineData(-1f)]
    [InlineData(0f)]
    public void InputBatch_AxesComeBackQuantizedExactlyLikeTheClientPredicts(float axis)
    {
        var batch = new InputBatch(1, [new PlayerInput(2, axis, -axis, 0.5f, 0.25f, InputButtons.None)]);

        var decoded = Assert.IsType<InputBatch>(MessageCodec.Read(Kit.Encode(batch)));

        Assert.Equal(Quantization.DequantizeAxis(Quantization.QuantizeAxis(axis)), decoded.Inputs[0].MoveX);
        Assert.Equal(Quantization.DequantizeAxis(Quantization.QuantizeAxis(-axis)), decoded.Inputs[0].MoveY);
        Assert.Equal(0.5f, decoded.Inputs[0].Yaw);
        Assert.Equal(0.25f, decoded.Inputs[0].Pitch);
    }

    [Fact]
    public void InputBatch_AxesBeyondOne_AreClamped()
    {
        var batch = new InputBatch(1, [new PlayerInput(2, 5f, -5f, 0f, 0f, InputButtons.None)]);

        var decoded = Assert.IsType<InputBatch>(MessageCodec.Read(Kit.Encode(batch)));

        Assert.Equal(1f, decoded.Inputs[0].MoveX);
        Assert.Equal(-1f, decoded.Inputs[0].MoveY);
    }

    private static Snapshot OneEntity(EntityState state) =>
        new(0, new Tick(0), 0, 0, [new SnapshotFrame(new FrameRef(FrameKind.Planet, 9), 0)], [state]);

    [Fact]
    public void Snapshot_EntityWithOnlyRotation_UsesFourBytesForTheCompressedRotation()
    {
        var snapshot = OneEntity(new EntityState(new EntityId(42), EntityStateFields.Rotation, 0, 0, default, Quaternion.Identity, default, 0));

        // Header 0E 01 + four zero varuints, one frame (Planet 2, id 9, seq 0), one entity: 2A, Fields 02, FrameIndex 0, ParentSeq 0,
        // packed identity 0xE0080200 little-endian.
        var expected = Kit.Hex("0E 01  00 00 00 00  01  02 09 00  01  2A 02 00 00  00 02 08 E0");
        Assert.Equal(expected, Kit.Encode(snapshot));
    }

    [Fact]
    public void Snapshot_EntityWithOnlyMotionFlags_UsesOneByte()
    {
        var snapshot = OneEntity(new EntityState(new EntityId(42), EntityStateFields.MotionFlags, 0, 0, default, Quaternion.Identity, default, 0xA5));

        Assert.Equal(Kit.Hex("0E 01  00 00 00 00  01  02 09 00  01  2A 08 00 00  A5"), Kit.Encode(snapshot));
    }

    [Fact]
    public void Snapshot_EntityWithNoFields_HasOnlyTheFourByteHeader()
    {
        var snapshot = OneEntity(new EntityState(new EntityId(42), EntityStateFields.None, 0, 0, default, Quaternion.Identity, default, 0));

        Assert.Equal(Kit.Hex("0E 01  00 00 00 00  01  02 09 00  01  2A 00 00 00"), Kit.Encode(snapshot));
    }

    [Fact]
    public void Snapshot_FieldsOrderIsPositionRotationVelocityMotionFlags()
    {
        var all = EntityStateFields.Position | EntityStateFields.Rotation | EntityStateFields.Velocity | EntityStateFields.MotionFlags;
        var snapshot = OneEntity(new EntityState(new EntityId(42), all, 0, 0, new Vector3(1, 2, 3), Quaternion.Identity, new Vector3(1, -2, 0), 7));

        Assert.Equal(
            Kit.Hex("0E 01  00 00 00 00  01  02 09 00  01  2A 0F 00 00  00 00 80 3F 00 00 00 40 00 00 40 40  00 02 08 E0  00 3C 00 C0 00 00  07"),
            Kit.Encode(snapshot));
    }

    [Fact]
    public void Snapshot_EntityStateVelocity_ComesBackRoundedToHalfPrecision()
    {
        var snapshot = OneEntity(new EntityState(new EntityId(42), EntityStateFields.Velocity, 0, 0, default, Quaternion.Identity, new Vector3(0.1f, 1234.567f, -3.14159f), 0));

        var decoded = Assert.IsType<Snapshot>(MessageCodec.Read(Kit.Encode(snapshot)));

        Assert.Equal(new Vector3((float)(Half)0.1f, (float)(Half)1234.567f, (float)(Half)(-3.14159f)), decoded.Entities[0].Velocity);
    }

    [Fact]
    public void Snapshot_EntityRotation_ComesBackWithinPointThreeDegrees()
    {
        var rotation = Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(1.1f, -0.4f, 2.3f));
        var snapshot = OneEntity(new EntityState(new EntityId(42), EntityStateFields.Rotation, 0, 0, default, rotation, default, 0));

        var decoded = Assert.IsType<Snapshot>(MessageCodec.Read(Kit.Encode(snapshot)));

        Assert.True(Kit.AngleDegrees(rotation, decoded.Entities[0].Rotation) < 0.3);
    }

    [Fact]
    public void Snapshot_EntityStateRotation_UsesTheSameBitsAsQuantizationPackRotation()
    {
        var rotation = Quaternion.Normalize(new Quaternion(0.3f, -0.5f, 0.1f, 0.8f));
        var snapshot = OneEntity(new EntityState(new EntityId(42), EntityStateFields.Rotation, 0, 0, default, rotation, default, 0));

        var frame = Kit.Encode(snapshot);

        var packed = BitConverter.ToUInt32(frame, frame.Length - 4);
        Assert.Equal(Quantization.PackRotation(rotation), packed);
    }
}
