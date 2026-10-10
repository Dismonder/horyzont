using System.Numerics;

using FsCheck.Xunit;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Testing.Generators;

namespace Horyzont.Core.Tests.Frames;

public class FrameMathTests
{
    private const int Cases = 2000;
    private const double Tolerance = 1e-9;

    [Property(MaxTest = Cases, Arbitrary = [typeof(FrameGen.Arbitraries)])]
    public void FrameMath_ComposeInverse_RoundTripWithin1e9(PoseD a, PoseD b, PoseD c, Vector3D point)
    {
        // Inverse: both orders give the identity.
        AssertIdentity(FrameMath.Compose(FrameMath.Inverse(a), a), Scale(a, a, a, point));
        AssertIdentity(FrameMath.Compose(a, FrameMath.Inverse(a)), Scale(a, a, a, point));

        // Associativity, compared on the transformed point (relative to the magnitudes involved).
        var left = FrameMath.Compose(FrameMath.Compose(a, b), c);
        var right = FrameMath.Compose(a, FrameMath.Compose(b, c));
        AssertClose(FrameMath.TransformPoint(left, point), FrameMath.TransformPoint(right, point), Scale(a, b, c, point));
        AssertRotationClose(left.Rotation, right.Rotation);

        // Composition rotations stay normalized.
        Assert.InRange(left.Rotation.Length(), 1 - 1e-12, 1 + 1e-12);

        // Relative(from, to) expresses "to" in "from".
        var relative = FrameMath.Relative(a, b);
        AssertClose(
            FrameMath.TransformPoint(FrameMath.Compose(a, relative), point), FrameMath.TransformPoint(b, point), Scale(a, b, c, point));
        AssertClose(
            FrameMath.TransformPoint(relative, point),
            FrameMath.TransformPoint(FrameMath.Inverse(a), FrameMath.TransformPoint(b, point)),
            Scale(a, b, c, point));
    }

    [Property(MaxTest = Cases, Arbitrary = [typeof(FrameGen.Arbitraries)])]
    public void FrameMath_Rotation_MatchesSystemNumericsConvention(QuaternionD a, QuaternionD b)
    {
        var fa = new Quaternion((float)a.X, (float)a.Y, (float)a.Z, (float)a.W);
        var fb = new Quaternion((float)b.X, (float)b.Y, (float)b.Z, (float)b.W);
        var v = new Vector3(3f, -7f, 11f);

        var expected = Vector3.Transform(Vector3.Transform(v, fb), fa);
        var product = a * b;
        var actual = product.Rotate(new Vector3D(v.X, v.Y, v.Z));

        Assert.Equal(expected.X, actual.X, 1e-3);
        Assert.Equal(expected.Y, actual.Y, 1e-3);
        Assert.Equal(expected.Z, actual.Z, 1e-3);
        var numerics = fa * fb;
        Assert.InRange(Math.Abs((numerics.X * product.X) + (numerics.Y * product.Y) + (numerics.Z * product.Z) + (numerics.W * product.W)), 1 - 1e-5, 1 + 1e-5);
    }

    [Fact]
    public void FrameMath_ComposeChain_PreservesMillimetresFarFromOrigin()
    {
        // A construct 400 km out in the system frame, a player 2 m into it: the world position keeps sub-millimetre accuracy.
        var ship = new PoseD(new Vector3D(400_000.123, -250_000.456, 1.5), QuaternionD.FromAxisAngle(new Vector3D(0, 1, 0), 0.7));
        var local = new PoseD(new Vector3D(2.001, 0.5, -1.25), QuaternionD.Identity);
        var world = FrameMath.Compose(ship, local);
        var back = FrameMath.Relative(ship, world);

        Assert.Equal(2.001, back.Position.X, 1e-9);
        Assert.Equal(0.5, back.Position.Y, 1e-9);
        Assert.Equal(-1.25, back.Position.Z, 1e-9);
    }

    [Fact]
    public void FrameMath_FromFloat_ExactPositionNormalizedRotation()
    {
        var position = new Vector3(1.1f, -99_999.5f, 0.3f);
        var rotation = Quaternion.Normalize(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f));

        var pose = FrameMath.FromFloat(position, rotation);

        Assert.Equal((double)position.X, pose.Position.X);
        Assert.Equal((double)position.Y, pose.Position.Y);
        Assert.Equal(rotation.W, pose.Rotation.W, 1e-7);
        Assert.Equal(1, pose.Rotation.Length(), 1e-15);
        Assert.True(FrameMath.TryToFloat(pose, FrameKind.Planet, out var p, out var r));
        Assert.Equal(position, p);
        Assert.Equal(rotation.X, r.X, 1e-7f);
        Assert.Equal(rotation.W, r.W, 1e-7f);
    }

    [Fact]
    public void FrameMath_ToFloat_RejectsNonFiniteAndOutOfRange()
    {
        var ok = new PoseD(new Vector3D(1, 2, 3), QuaternionD.Identity);
        Assert.True(FrameMath.TryToFloat(ok, FrameKind.Planet, out _, out var identity));
        Assert.Equal(Quaternion.Identity, identity);

        Assert.False(FrameMath.TryToFloat(ok with { Position = new Vector3D(double.NaN, 0, 0) }, FrameKind.Planet, out _, out _));
        Assert.False(FrameMath.TryToFloat(ok with { Position = new Vector3D(0, double.PositiveInfinity, 0) }, FrameKind.Planet, out _, out _));
        Assert.False(FrameMath.TryToFloat(ok with { Rotation = new QuaternionD(0, 0, 0, double.NaN) }, FrameKind.Planet, out _, out _));
        Assert.False(FrameMath.TryToFloat(ok with { Rotation = new QuaternionD(0, 0, 0, 2) }, FrameKind.Planet, out _, out _));
        Assert.False(FrameMath.TryToFloat(ok with { Rotation = default }, FrameKind.Planet, out _, out _));

        // Range per frame kind (FrameLimits).
        var planetEdge = ok with { Position = new Vector3D(0, 0, FrameLimits.PlanetMaxCoordinate) };
        var planetOut = ok with { Position = new Vector3D(0, 0, -FrameLimits.PlanetMaxCoordinate - 0.01) };
        var systemEdge = ok with { Position = new Vector3D(FrameLimits.SystemMaxCoordinate, 0, 0) };
        var systemOut = ok with { Position = new Vector3D(FrameLimits.SystemMaxCoordinate + 0.01, 0, 0) };
        Assert.True(FrameMath.TryToFloat(planetEdge, FrameKind.Planet, out _, out _));
        Assert.False(FrameMath.TryToFloat(planetOut, FrameKind.Planet, out _, out _));
        Assert.True(FrameMath.TryToFloat(planetEdge, FrameKind.Construct, out _, out _));
        Assert.False(FrameMath.TryToFloat(planetOut, FrameKind.Construct, out _, out _));
        Assert.True(FrameMath.TryToFloat(planetOut, FrameKind.System, out _, out _));
        Assert.True(FrameMath.TryToFloat(systemEdge, FrameKind.System, out _, out _));
        Assert.False(FrameMath.TryToFloat(systemOut, FrameKind.System, out _, out _));
        Assert.False(FrameMath.TryToFloat(ok, (FrameKind)0, out _, out _));

        Assert.Equal(100_000, FrameLimits.PlanetMaxCoordinate);
        Assert.Equal(100_000, FrameLimits.ConstructMaxCoordinate);
        Assert.Equal(500_000, FrameLimits.SystemMaxCoordinate);
    }

    [Fact]
    public void FrameMath_ToFloat_NormalizesSlightlyOffRotations()
    {
        var pose = new PoseD(Vector3D.Zero, new QuaternionD(0, 0, 0, 1 + 1e-9));

        Assert.True(FrameMath.TryToFloat(pose, FrameKind.Planet, out _, out var rotation));
        Assert.Equal(1f, rotation.W);
    }

    [Fact]
    public void QuaternionD_Slerp_HalfwayAndShortestPath()
    {
        var a = QuaternionD.Identity;
        var b = QuaternionD.FromAxisAngle(new Vector3D(0, 0, 1), Math.PI / 2);

        var half = QuaternionD.Slerp(a, b, 0.5);
        var expected = QuaternionD.FromAxisAngle(new Vector3D(0, 0, 1), Math.PI / 4);
        AssertRotationClose(expected, half);

        // The negated end is the same rotation: slerp takes the short way.
        AssertRotationClose(expected, QuaternionD.Slerp(a, -b, 0.5));
        AssertRotationClose(a, QuaternionD.Slerp(a, b, 0));
        AssertRotationClose(b, QuaternionD.Slerp(a, b, 1));
    }

    [Fact]
    public void FrameMath_Operations_AllocateZeroBytes()
    {
        var a = new PoseD(new Vector3D(1, 2, 3), QuaternionD.FromAxisAngle(new Vector3D(1, 0, 0), 0.3));
        var b = new PoseD(new Vector3D(-4, 5, 6), QuaternionD.FromAxisAngle(new Vector3D(0, 1, 0), 0.2));
        var sink = 0.0;
        for (var i = 0; i < 100; i++)
        {
            sink += Step(a, b);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            sink += Step(a, b);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(double.IsFinite(sink));

        static double Step(in PoseD a, in PoseD b)
        {
            var c = FrameMath.Compose(a, b);
            var r = FrameMath.Relative(a, c);
            var p = FrameMath.TransformPoint(r, new Vector3D(1, 1, 1));
            FrameMath.TryToFloat(r, FrameKind.Planet, out var fp, out _);
            return p.X + fp.X + QuaternionD.Slerp(a.Rotation, b.Rotation, 0.3).W;
        }
    }

    private static double Scale(in PoseD a, in PoseD b, in PoseD c, Vector3D point) =>
        Math.Max(1, a.Position.Length() + b.Position.Length() + c.Position.Length() + point.Length());

    private static void AssertIdentity(in PoseD pose, double scale)
    {
        Assert.InRange(pose.Position.Length(), 0, Tolerance * scale);
        AssertRotationClose(QuaternionD.Identity, pose.Rotation);
    }

    private static void AssertClose(Vector3D expected, Vector3D actual, double scale) =>
        Assert.InRange((expected - actual).Length(), 0, Tolerance * scale);

    private static void AssertRotationClose(QuaternionD expected, QuaternionD actual) =>
        Assert.InRange(1 - Math.Abs(QuaternionD.Dot(expected, actual)), -Tolerance, Tolerance);
}
