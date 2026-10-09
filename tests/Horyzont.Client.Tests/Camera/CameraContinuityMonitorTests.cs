using System.Numerics;

using Horyzont.Client.Camera;

namespace Horyzont.Client.Tests.Camera;

public sealed class CameraContinuityMonitorTests
{
    private const float Dt = 1f / 60f;

    private static Vector3 Direction(float degrees) =>
        new(MathF.Cos(degrees * MathF.PI / 180f), 0f, MathF.Sin(degrees * MathF.PI / 180f));

    private static CameraContinuityMonitor Monitor() => new(maxSpeedMetersPerSecond: 7.5f, tickSeconds: Dt);

    [Fact]
    public void CameraContinuityMonitor_SmoothMotion_NoReports()
    {
        var monitor = Monitor();

        for (var tick = 0; tick < 600; tick++)
        {
            Assert.False(monitor.Observe(new Vector3(0.125f * tick, 0f, 0f), Direction(tick * 0.5f), 0f));
        }

        Assert.Equal(0, monitor.JumpCount);
    }

    [Fact]
    public void CameraContinuityMonitor_FlagsJump_IgnoresMouseAndDeclaredSnaps()
    {
        var monitor = Monitor();
        Assert.False(monitor.Observe(Vector3.Zero, Direction(0f), 0f));

        // A 5 degree turn without the mouse is a jump; the same turn explained by the mouse is not.
        Assert.True(monitor.Observe(Vector3.Zero, Direction(5f), 0f));
        Assert.Equal(5f * MathF.PI / 180f, monitor.LastJumpAngle, 1e-4f);
        Assert.False(monitor.Observe(Vector3.Zero, Direction(10f), 5f * MathF.PI / 180f));

        // A displacement beyond speed * dt + 1 cm is a jump.
        Assert.True(monitor.Observe(new Vector3(1f, 0f, 0f), Direction(10f), 0f));
        Assert.Equal(1f, monitor.LastJumpDistance, 1e-5f);
        Assert.Equal(2, monitor.JumpCount);

        // A declared snap is not reported, and neither is the tick after it.
        monitor.Snap();
        Assert.False(monitor.Observe(new Vector3(500f, 0f, 0f), Direction(120f), 0f));
        Assert.False(monitor.Observe(new Vector3(500.1f, 0f, 0f), Direction(120.5f), 0f));
        Assert.Equal(2, monitor.JumpCount);
    }

    [Fact]
    public void CameraContinuityMonitor_FirstObservation_IsBaseline()
    {
        var monitor = Monitor();

        Assert.False(monitor.Observe(new Vector3(1000f, 0f, 0f), Direction(90f), 0f));
        Assert.Equal(0, monitor.JumpCount);
    }

    [Fact]
    public void CameraContinuityMonitor_StepsJustInsideTheLimits_AreNotJumps()
    {
        var monitor = Monitor();
        monitor.Observe(Vector3.Zero, Direction(0f), 0f);

        Assert.False(monitor.Observe(new Vector3(7.5f * Dt, 0f, 0f), Direction(1.99f), 0f));
        Assert.Equal(0, monitor.JumpCount);
    }
}
