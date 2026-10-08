using System.Numerics;

namespace Horyzont.Core.Serialization;

/// <summary>Lossy encodings shared by client prediction and the wire format.</summary>
public static class Quantization
{
    private const float InvSqrt2 = 0.70710678f;
    private const float Sqrt2 = 1.41421356f;

    /// <summary>Quantizes a movement axis to -127..127.</summary>
    /// <param name="v">Axis value, nominally -1..1.</param>
    /// <returns>The quantized axis.</returns>
    public static sbyte QuantizeAxis(float v)
    {
        if (float.IsNaN(v))
        {
            throw new ArgumentException("Axis value is NaN.", nameof(v));
        }

        return (sbyte)Math.Clamp((int)MathF.Round(v * 127f), -127, 127);
    }

    /// <summary>Restores a movement axis; -128 is a <see cref="ProtocolViolationException"/>.</summary>
    /// <param name="q">Quantized axis.</param>
    /// <returns>The axis value.</returns>
    public static float DequantizeAxis(sbyte q)
    {
        if (q == -128)
        {
            throw new ProtocolViolationException("Quantized axis value cannot be -128.");
        }

        return q / 127f;
    }

    /// <summary>Packs a unit quaternion into 32 bits (smallest three); throws <see cref="ArgumentException"/> when not normalized.</summary>
    /// <param name="q">Rotation.</param>
    /// <returns>The packed value.</returns>
    public static uint PackRotation(Quaternion q)
    {
        float len = q.Length();
        if (!float.IsFinite(len) || Math.Abs(len - 1f) > 1e-3f)
        {
            throw new ArgumentException("Quaternion must be finite and normalized to unit length.", nameof(q));
        }

        q = Quaternion.Normalize(q);

        float x = q.X;
        float y = q.Y;
        float z = q.Z;
        float w = q.W;

        float ax = MathF.Abs(x);
        float ay = MathF.Abs(y);
        float az = MathF.Abs(z);
        float aw = MathF.Abs(w);

        int idx = 0;
        float maxVal = ax;

        if (ay > maxVal)
        {
            maxVal = ay;
            idx = 1;
        }

        if (az > maxVal)
        {
            maxVal = az;
            idx = 2;
        }

        if (aw > maxVal)
        {
            idx = 3;
        }

        float c = idx switch
        {
            0 => x,
            1 => y,
            2 => z,
            _ => w,
        };

        if (c < 0f)
        {
            x = -x;
            y = -y;
            z = -z;
            w = -w;
        }

        float a, b, compC;
        switch (idx)
        {
            case 0:
                a = y;
                b = z;
                compC = w;
                break;
            case 1:
                a = x;
                b = z;
                compC = w;
                break;
            case 2:
                a = x;
                b = y;
                compC = w;
                break;
            default:
                a = x;
                b = y;
                compC = z;
                break;
        }

        static uint PackComponent(float s)
        {
            float n = (s + InvSqrt2) / Sqrt2;
            int k = Math.Clamp((int)MathF.Round(n * 1023f), 0, 1023);
            return (uint)k;
        }

        uint ka = PackComponent(a);
        uint kb = PackComponent(b);
        uint kc = PackComponent(compC);

        return ((uint)idx << 30) | (ka << 20) | (kb << 10) | kc;
    }

    /// <summary>Unpacks a rotation; a packed value with a squared sum above 1 is a <see cref="ProtocolViolationException"/>.</summary>
    /// <param name="packed">Packed value.</param>
    /// <returns>The rotation.</returns>
    public static Quaternion UnpackRotation(uint packed)
    {
        int idx = (int)(packed >> 30);
        int ka = (int)((packed >> 20) & 0x3FF);
        int kb = (int)((packed >> 10) & 0x3FF);
        int kc = (int)(packed & 0x3FF);

        static float UnpackComponent(int k) =>
            (k / 1023f * Sqrt2) - InvSqrt2;

        float a = UnpackComponent(ka);
        float b = UnpackComponent(kb);
        float c = UnpackComponent(kc);

        float sumSq = (a * a) + (b * b) + (c * c);
        if (sumSq > 1f)
        {
            throw new ProtocolViolationException($"Sum of squares in unpacked rotation exceeds 1 ({sumSq}).");
        }

        float d = MathF.Sqrt(MathF.Max(0f, 1f - sumSq));

        float x, y, z, w;
        switch (idx)
        {
            case 0:
                x = d;
                y = a;
                z = b;
                w = c;
                break;
            case 1:
                x = a;
                y = d;
                z = b;
                w = c;
                break;
            case 2:
                x = a;
                y = b;
                z = d;
                w = c;
                break;
            case 3:
                x = a;
                y = b;
                z = c;
                w = d;
                break;
            default:
                throw new ProtocolViolationException("Invalid rotation component index.");
        }

        return new Quaternion(x, y, z, w);
    }
}
