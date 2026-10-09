using System.Numerics;

using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.Motion;
using Horyzont.Core.Tests.Serialization;

namespace Horyzont.Core.Tests.Motion;

/// <summary>K2-11 step 1: the tangent frame and the meaning of <c>PlayerInput.Yaw</c>.</summary>
public sealed class SurfaceFrameTests
{
    private const float Pi = MathF.PI;

    /// <summary>A unit direction with |y| below 1 - 1e-6, so that it is not at a pole.</summary>
    private static Gen<Vector3> NonPoleUp { get; } =
        Gen.Choose(-999_998, 999_998).SelectMany(yi => Gen.Choose(0, 1_000_000).Select(phi =>
        {
            var y = yi / 1_000_000f;
            var r = MathF.Sqrt(1f - (y * y));
            var angle = phi / 1_000_000f * 2f * Pi;
            return new Vector3(r * MathF.Cos(angle), y, r * MathF.Sin(angle));
        }));

    /// <summary>A yaw in (-pi, pi].</summary>
    private static Gen<float> AnyYaw { get; } = Gen.Choose(-999_999, 1_000_000).Select(i => i / 1_000_000f * Pi);

    private static float Wrap(float angle)
    {
        while (angle > Pi)
        {
            angle -= 2f * Pi;
        }

        while (angle <= -Pi)
        {
            angle += 2f * Pi;
        }

        return angle;
    }

    private static float AngleBetween(Vector3 a, Vector3 b) => MathF.Atan2(Vector3.Cross(a, b).Length(), Vector3.Dot(a, b));

    private static void AssertClose(Vector3 expected, Vector3 actual, float tolerance) =>
        Assert.True((expected - actual).Length() <= tolerance, $"expected {expected}, actual {actual}, tolerance {tolerance}");

    private static void AssertUnit(Quaternion q, float tolerance) =>
        Assert.True(float.IsFinite(q.Length()) && MathF.Abs(q.Length() - 1f) <= tolerance, $"not unit: {q} (length {q.Length()})");

    private static Vector3 Forward(Quaternion q) => Vector3.Transform(-Vector3.UnitZ, q);

    private static Vector3 Up(Quaternion q) => Vector3.Transform(Vector3.UnitY, q);

    [Fact]
    public void SurfaceFrame_FromYawThenYawOf_RoundTrips() =>
        Kit.ForAll(
            NonPoleUp.SelectMany(up => AnyYaw.Select(yaw => (up, yaw))),
            pair =>
            {
                var back = SurfaceFrame.YawOf(SurfaceFrame.FromYaw(pair.up, pair.yaw), pair.up);

                Assert.True(MathF.Abs(Wrap(back - pair.yaw)) <= 1e-4f, $"up {pair.up}, yaw {pair.yaw}, back {back}");
            });

    [Fact]
    public void SurfaceFrame_FromYaw_BodyUpEqualsSurfaceUp() =>
        Kit.ForAll(
            NonPoleUp.SelectMany(up => AnyYaw.Select(yaw => (up, yaw))),
            pair =>
            {
                var q = SurfaceFrame.FromYaw(pair.up, pair.yaw);

                AssertClose(pair.up, Up(q), 1e-5f);
                AssertUnit(q, 1e-6f);
            });

    [Fact]
    public void SurfaceFrame_Reference_IsUnitAndTangentToUp() =>
        Kit.ForAll(
            NonPoleUp,
            up =>
            {
                var reference = SurfaceFrame.Reference(up);

                Assert.True(MathF.Abs(reference.Length() - 1f) <= 1e-5f);
                Assert.True(MathF.Abs(Vector3.Dot(reference, up)) <= 1e-5f);
            });

    [Fact]
    public void SurfaceFrame_YawConvention_ZeroIsNorthAndPositiveTurnsRightHandedAboutUp()
    {
        var up = Vector3.UnitX;

        // North at the equator point +X is +Y; yaw 0 looks along it (body forward = -Z).
        AssertClose(Vector3.UnitY, SurfaceFrame.Reference(up), 1e-6f);
        AssertClose(Vector3.UnitY, Forward(SurfaceFrame.FromYaw(up, 0f)), 1e-6f);

        // Right-handed about up: +90 degrees takes north (+Y) to up x north = +X x +Y = +Z.
        AssertClose(Vector3.UnitZ, Forward(SurfaceFrame.FromYaw(up, Pi / 2f)), 1e-5f);
        AssertClose(-Vector3.UnitY, Forward(SurfaceFrame.FromYaw(up, Pi)), 1e-5f);
    }

    [Fact]
    public void SurfaceFrame_FromYaw_BodyIsRightHanded()
    {
        var q = SurfaceFrame.FromYaw(Vector3.UnitX, 0f);

        // Body +X (right) must be forward x up for a right-handed basis with forward = -Z.
        AssertClose(Vector3.Cross(Forward(q), Up(q)), Vector3.Transform(Vector3.UnitX, q), 1e-6f);
    }

    [Fact]
    public void SurfaceFrame_AtExactPoles_FiniteAndNormalized()
    {
        var ups = new List<Vector3> { Vector3.UnitY, -Vector3.UnitY };
        foreach (var sign in new[] { 1f, -1f })
        {
            ups.Add(Vector3.Normalize(new Vector3(1e-7f, sign, 0f)));
            ups.Add(Vector3.Normalize(new Vector3(0f, sign, -1e-7f)));
            ups.Add(Vector3.Normalize(new Vector3(-1e-7f, sign, 1e-7f)));
        }

        foreach (var up in ups)
        {
            var reference = SurfaceFrame.Reference(up);
            Assert.True(float.IsFinite(reference.X) && float.IsFinite(reference.Y) && float.IsFinite(reference.Z), $"Reference({up})");
            Assert.True(MathF.Abs(reference.Length() - 1f) <= 1e-5f);
            Assert.True(MathF.Abs(Vector3.Dot(reference, up)) <= 1e-5f);

            foreach (var yaw in new[] { -3f, -1f, 0f, 0.5f, 3f, Pi })
            {
                var q = SurfaceFrame.FromYaw(up, yaw);
                AssertUnit(q, 1e-6f);
                var back = SurfaceFrame.YawOf(q, up);
                Assert.True(float.IsFinite(back) && back > -Pi - 1e-6f && back <= Pi + 1e-6f, $"YawOf at {up}: {back}");
                Assert.True(MathF.Abs(Wrap(back - yaw)) <= 1e-3f, $"pole round trip at {up}: yaw {yaw}, back {back}");
            }
        }
    }

    [Fact]
    public void SurfaceFrame_PoleFallback_UsesPlusZ()
    {
        AssertClose(Vector3.UnitZ, SurfaceFrame.Reference(Vector3.UnitY), 1e-6f);
        AssertClose(Vector3.UnitZ, SurfaceFrame.Reference(-Vector3.UnitY), 1e-6f);
    }

    [Fact]
    public void SurfaceFrame_YawOf_DegenerateForwardOnUp_IsZero()
    {
        // Body looking straight along "up": the tangent projection of forward is zero.
        var q = Quaternion.CreateFromAxisAngle(Vector3.UnitX, Pi / 2f);

        var yaw = SurfaceFrame.YawOf(q, Forward(q));

        Assert.Equal(0f, yaw);
    }

    [Fact]
    public void SurfaceFrame_TransportToUp_SmallStep_HeadingContinuous()
    {
        // A great circle through both poles (in the XY plane), 200 degrees in steps of 0.01 degrees.
        const float step = 0.01f * Pi / 180f;
        const int steps = 20_000;
        var q = SurfaceFrame.FromYaw(Vector3.UnitX, 0.3f);
        var previous = Forward(q);

        for (var i = 1; i <= steps; i++)
        {
            var theta = i * step;
            var up = new Vector3(MathF.Cos(theta), MathF.Sin(theta), 0f);
            q = SurfaceFrame.TransportToUp(q, up);
            var forward = Forward(q);

            Assert.True(AngleBetween(previous, forward) <= step + 1e-5f, $"step {i}: heading changed by {AngleBetween(previous, forward)}");
            AssertClose(up, Up(q), 1e-4f);
            AssertUnit(q, 1e-5f);
            previous = forward;
        }
    }

    [Fact]
    public void SurfaceFrame_TransportToUp_SameUp_ReturnsSameRotation()
    {
        var q = SurfaceFrame.FromYaw(Vector3.Normalize(new Vector3(1f, 2f, 3f)), 1.1f);

        var moved = SurfaceFrame.TransportToUp(q, Up(q));

        AssertClose(Forward(q), Forward(moved), 1e-5f);
        AssertUnit(moved, 1e-6f);
    }

    [Fact]
    public void SurfaceFrame_TransportToUp_OppositeUp_FiniteAndFlipsUp()
    {
        var q = SurfaceFrame.FromYaw(Vector3.UnitX, 0.4f);

        var moved = SurfaceFrame.TransportToUp(q, -Vector3.UnitX);

        AssertUnit(moved, 1e-5f);
        AssertClose(-Vector3.UnitX, Up(moved), 1e-4f);
    }

    [Fact]
    public void SurfaceFrame_TransportToUp_Random_BodyUpEqualsNewUpAndTurnIsMinimal() =>
        Kit.ForAll(
            NonPoleUp.SelectMany(a => NonPoleUp.SelectMany(b => AnyYaw.Select(yaw => (a, b, yaw)))),
            t =>
            {
                var q = SurfaceFrame.FromYaw(t.a, t.yaw);
                var moved = SurfaceFrame.TransportToUp(q, t.b);

                AssertClose(t.b, Up(moved), 1e-4f);
                AssertUnit(moved, 1e-5f);

                // The rotation applied is the shortest one between the two ups, so the body turns by exactly that angle.
                var turned = Quaternion.Concatenate(Quaternion.Inverse(q), moved);
                var angle = 2f * MathF.Acos(MathF.Min(1f, MathF.Abs(turned.W)));
                Assert.True(MathF.Abs(angle - AngleBetween(t.a, t.b)) <= 2e-3f, $"turn {angle}, ups {AngleBetween(t.a, t.b)}");
            });
}
