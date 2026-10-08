using System.Numerics;

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

using Horyzont.Core.Serialization;
using Horyzont.Testing.Generators;

namespace Horyzont.Core.Tests.Serialization;

public sealed class QuantizationTests
{
    private static Quaternion RandomUnit(Random rng)
    {
        double Gauss()
        {
            var a = 1.0 - rng.NextDouble();
            var b = rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(a)) * Math.Cos(2.0 * Math.PI * b);
        }

        var q = new Quaternion((float)Gauss(), (float)Gauss(), (float)Gauss(), (float)Gauss());
        return Quaternion.Normalize(q);
    }

    [Fact]
    public void QuantizeAxis_MinusOneZeroOne_AreExact()
    {
        Assert.Equal((sbyte)-127, Quantization.QuantizeAxis(-1f));
        Assert.Equal((sbyte)0, Quantization.QuantizeAxis(0f));
        Assert.Equal((sbyte)127, Quantization.QuantizeAxis(1f));
        Assert.Equal(-1f, Quantization.DequantizeAxis(-127));
        Assert.Equal(0f, Quantization.DequantizeAxis(0));
        Assert.Equal(1f, Quantization.DequantizeAxis(127));
    }

    [Theory]
    [InlineData(0.5f, 64)]
    [InlineData(-0.5f, -64)]
    [InlineData(0.3f, 38)]
    [InlineData(0.004f, 1)]
    [InlineData(0.003f, 0)]
    [InlineData(2f, 127)]
    [InlineData(-2f, -127)]
    [InlineData(1000f, 127)]
    [InlineData(-1000f, -127)]
    public void QuantizeAxis_FollowsRoundAndClampFormula(float value, int expected) =>
        Assert.Equal((sbyte)expected, Quantization.QuantizeAxis(value));

    [Fact]
    public void QuantizeAxis_NeverProducesMinusOneTwentyEight()
    {
        for (var i = -300; i <= 300; i++)
        {
            Assert.NotEqual((sbyte)-128, Quantization.QuantizeAxis(i / 100f));
        }
    }

    [Fact]
    public void DequantizeAxis_MinusOneTwentyEight_ThrowsProtocolViolation() =>
        Assert.Throws<ProtocolViolationException>(() => Quantization.DequantizeAxis(-128));

    [Fact]
    public void DequantizeAxis_IsQuantizedValueDividedBy127()
    {
        for (var q = -127; q <= 127; q++)
        {
            Assert.Equal(q / 127f, Quantization.DequantizeAxis((sbyte)q));
        }
    }

    [Property(MaxTest = Kit.Cases, Arbitrary = [typeof(UnitRangeArbitrary)])]
    public void Axis_QuantizeThenDequantize_IsWithinHalfStep(float value)
    {
        var back = Quantization.DequantizeAxis(Quantization.QuantizeAxis(value));

        Assert.InRange(Math.Abs(back - value), 0f, (0.5f / 127f) + 1e-6f);
        Assert.Equal(Quantization.QuantizeAxis(value), Quantization.QuantizeAxis(back));
    }

    [Fact]
    public void PackRotation_Identity_MatchesHandComputedValue() =>
        // Largest component W (index 3, positive); the other three are 0 -> n = 0.5 -> round-half-even(511.5) = 512.
        // packed = 3 << 30 | 512 << 20 | 512 << 10 | 512 = 0xE0080200.
        Assert.Equal(0xE0080200u, Quantization.PackRotation(Quaternion.Identity));

    [Theory]
    [InlineData(1f, 0f, 0f, 0f, 0x20080200u)]
    [InlineData(0f, 1f, 0f, 0f, 0x60080200u)]
    [InlineData(0f, 0f, 1f, 0f, 0xA0080200u)]
    [InlineData(0f, 0f, 0f, 1f, 0xE0080200u)]
    [InlineData(0f, 0f, 0f, -1f, 0xE0080200u)]
    [InlineData(-1f, 0f, 0f, 0f, 0x20080200u)]
    public void PackRotation_AxisAlignedRotations_PutLargestIndexInTopBitsAndFlipNegativeSign(float x, float y, float z, float w, uint expected)
    {
        Assert.Equal(expected, Quantization.PackRotation(new Quaternion(x, y, z, w)));
    }

    [Fact]
    public void PackRotation_TiedLargestComponents_ChoosesLowestIndex() =>
        // (0.5, 0.5, 0.5, 0.5): idx 0; the others n = (0.5 + 0.70710678) / 1.41421356 = 0.853553, k = round(873.18) = 873 = 0x369.
        Assert.Equal(0x369DA769u, Quantization.PackRotation(new Quaternion(0.5f, 0.5f, 0.5f, 0.5f)));

    [Property(MaxTest = Kit.Cases, Arbitrary = [typeof(UnitQuaternionArbitrary)])]
    public void PackRotation_QuaternionAndItsNegation_PackIdentically(Quaternion q) =>
        Assert.Equal(Quantization.PackRotation(q), Quantization.PackRotation(Quaternion.Negate(q)));

    [Property(MaxTest = Kit.Cases, Arbitrary = [typeof(UnitQuaternionArbitrary)])]
    public void Rotation_PackThenUnpack_IsWithinTolerance(Quaternion q)
    {
        var back = Quantization.UnpackRotation(Quantization.PackRotation(q));

        Assert.True(Kit.AngleDegrees(q, back) < 0.3, $"angle {Kit.AngleDegrees(q, back)} for {q}");
        Assert.InRange(back.Length(), 0.999f, 1.001f);
    }

    [Fact]
    public void Rotation_TenThousandRandomQuaternions_AreWithinPointThreeDegrees()
    {
        var rng = new Random(20_260_508);
        var worst = 0.0;
        for (var i = 0; i < 10_000; i++)
        {
            var q = RandomUnit(rng);
            var angle = Kit.AngleDegrees(q, Quantization.UnpackRotation(Quantization.PackRotation(q)));
            worst = Math.Max(worst, angle);
        }

        Assert.True(worst < 0.3, $"worst error {worst} degrees");
    }

    [Fact]
    public void PackRotation_SlightlyOffUnitLength_IsNormalizedAndAccepted()
    {
        var packedExact = Quantization.PackRotation(Quaternion.Identity);

        Assert.Equal(packedExact, Quantization.PackRotation(new Quaternion(0, 0, 0, 1.0005f)));
        Assert.Equal(packedExact, Quantization.PackRotation(new Quaternion(0, 0, 0, 0.9995f)));
    }

    [Theory]
    [InlineData(0f, 0f, 0f, 1.002f)]
    [InlineData(0f, 0f, 0f, 0.998f)]
    [InlineData(2f, 0f, 0f, 0f)]
    [InlineData(0f, 0f, 0f, 0f)]
    [InlineData(float.NaN, 0f, 0f, 1f)]
    public void PackRotation_NotNormalized_ThrowsArgumentException(float x, float y, float z, float w) =>
        Assert.ThrowsAny<ArgumentException>(() => Quantization.PackRotation(new Quaternion(x, y, z, w)));

    [Fact]
    public void UnpackRotation_ThreeLargestFieldsExceedUnitSphere_ThrowsProtocolViolation() =>
        // a = b = c = 1023 -> each stored component is 0.7071, so a^2 + b^2 + c^2 = 1.5 > 1.
        Assert.Throws<ProtocolViolationException>(() => Quantization.UnpackRotation(0x3FFFFFFFu));

    [Fact]
    public void UnpackRotation_ValidPackedValue_IsUnitLength()
    {
        var q = Quantization.UnpackRotation(0xE0080200u);

        Assert.InRange(q.Length(), 0.9999f, 1.0001f);
        Assert.True(Kit.AngleDegrees(q, Quaternion.Identity) < 0.3);
    }

    [Property(MaxTest = Kit.Cases)]
    public void UnpackRotation_AnyPackedValue_ReturnsUnitQuaternionOrThrowsProtocolViolation(uint packed)
    {
        try
        {
            var q = Quantization.UnpackRotation(packed);
            Assert.InRange(q.Length(), 0.999f, 1.001f);
        }
        catch (ProtocolViolationException)
        {
            // Allowed: a, b, c outside the unit sphere.
        }
    }

    public static class UnitRangeArbitrary
    {
        public static Arbitrary<float> Floats() => Arb.From(CodecGen.UnitRange);
    }

    public static class UnitQuaternionArbitrary
    {
        public static Arbitrary<Quaternion> Quaternions() => Arb.From(CodecGen.UnitQuaternion);
    }
}
