using System.Numerics;

using Horyzont.Client.Camera;

namespace Horyzont.Client.Tests.Camera;

public sealed class CameraUpSmootherTests
{
    private const float Dt = 1f / 60f;

    private static float AngleBetween(Vector3 a, Vector3 b) => MathF.Atan2(Vector3.Cross(a, b).Length(), Vector3.Dot(a, b));

    [Fact]
    public void CameraUpSmoother_90DegreeChange_ConvergesMonotonically_RateLimited()
    {
        var smoother = new CameraUpSmoother();
        smoother.Snap(Vector3.UnitX);
        var target = Vector3.UnitY;
        var previousAngle = AngleBetween(smoother.Up, target);
        const float maxStep = 3f * MathF.PI / 180f;

        for (var tick = 0; tick < 300; tick++)
        {
            var before = smoother.Up;
            smoother.Advance(target, Dt);

            var angle = AngleBetween(smoother.Up, target);
            Assert.True(angle <= previousAngle + 1e-6f, $"tick {tick}: angle grew from {previousAngle} to {angle}");
            Assert.True(AngleBetween(before, smoother.Up) <= maxStep + 1e-5f, $"tick {tick}: step {AngleBetween(before, smoother.Up)}");
            Assert.True(MathF.Abs(smoother.Up.Length() - 1f) < 1e-5f);
            previousAngle = angle;
        }

        Assert.True(previousAngle < 1e-3f * MathF.PI / 180f, $"final error {previousAngle}");
    }

    [Fact]
    public void CameraUpSmoother_FirstTickOfLargeChange_IsRateLimitedToThreeDegrees()
    {
        var smoother = new CameraUpSmoother();
        smoother.Snap(Vector3.UnitX);

        smoother.Advance(Vector3.UnitY, Dt);

        Assert.Equal(3f * MathF.PI / 180f, AngleBetween(Vector3.UnitX, smoother.Up), 1e-4f);
    }

    [Fact]
    public void CameraUpSmoother_Snap_Immediate()
    {
        var smoother = new CameraUpSmoother();
        var up = Vector3.Normalize(new Vector3(0.2f, -1f, 0.4f));

        smoother.Snap(up);

        Assert.Equal(up, smoother.Up);
    }

    [Fact]
    public void CameraUpSmoother_OppositeTarget_StaysFiniteAndConverges()
    {
        var smoother = new CameraUpSmoother();
        smoother.Snap(Vector3.UnitX);

        for (var tick = 0; tick < 600; tick++)
        {
            smoother.Advance(-Vector3.UnitX, Dt);
            Assert.True(float.IsFinite(smoother.Up.X) && float.IsFinite(smoother.Up.Y) && float.IsFinite(smoother.Up.Z));
        }

        Assert.True(AngleBetween(smoother.Up, -Vector3.UnitX) < 0.01f);
    }

    [Fact]
    public void CameraUpSmoother_AlreadyAtTarget_StaysPut()
    {
        var smoother = new CameraUpSmoother();
        smoother.Snap(Vector3.UnitZ);

        smoother.Advance(Vector3.UnitZ, Dt);

        Assert.Equal(Vector3.UnitZ, smoother.Up);
    }

    [Fact]
    public void CameraUpSmoother_Advance_DoesNotAllocate()
    {
        var smoother = new CameraUpSmoother();
        smoother.Snap(Vector3.UnitX);
        for (var i = 0; i < 100; i++)
        {
            smoother.Advance(Vector3.UnitY, Dt);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            smoother.Advance(i % 600 < 300 ? Vector3.UnitX : Vector3.UnitZ, Dt);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
