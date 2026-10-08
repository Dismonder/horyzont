using System.Numerics;

namespace Horyzont.Core.Serialization;

/// <summary>Lossy encodings shared by client prediction and the wire format.</summary>
public static class Quantization
{
    /// <summary>Quantizes a movement axis to -127..127.</summary>
    /// <param name="v">Axis value, nominally -1..1.</param>
    /// <returns>The quantized axis.</returns>
    public static sbyte QuantizeAxis(float v) => throw new NotImplementedException();

    /// <summary>Restores a movement axis; -128 is a <see cref="ProtocolViolationException"/>.</summary>
    /// <param name="q">Quantized axis.</param>
    /// <returns>The axis value.</returns>
    public static float DequantizeAxis(sbyte q) => throw new NotImplementedException();

    /// <summary>Packs a unit quaternion into 32 bits (smallest three); throws <see cref="ArgumentException"/> when not normalized.</summary>
    /// <param name="q">Rotation.</param>
    /// <returns>The packed value.</returns>
    public static uint PackRotation(Quaternion q) => throw new NotImplementedException();

    /// <summary>Unpacks a rotation; a packed value with a squared sum above 1 is a <see cref="ProtocolViolationException"/>.</summary>
    /// <param name="packed">Packed value.</param>
    /// <returns>The rotation.</returns>
    public static Quaternion UnpackRotation(uint packed) => throw new NotImplementedException();
}
