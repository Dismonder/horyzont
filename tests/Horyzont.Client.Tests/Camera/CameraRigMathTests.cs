using System.Numerics;

using Horyzont.Client.Camera;
using Horyzont.Core.Motion;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Camera;

public sealed class CameraRigMathTests
{
    private static readonly GameRules _rules = new();

    public static TheoryData<float, float, float> Positions => new()
    {
        { 4001f, 0f, 0f },
        { 100f, 3900f, -700f },
        { -1000f, -2000f, 3300f },
    };

    [Theory]
    [MemberData(nameof(Positions))]
    public void CameraRigMath_FirstPerson_OriginEqualsServerEye(float x, float y, float z)
    {
        // The server derives the eye from the stored PlayerState: put the same feet there and compare.
        var sim = new ServerSimulation(WorldFactory.CreateDefault(), new FakeTerrainService(), new InMemoryJournal(), _rules, NullLogger<ServerSimulation>.Instance);
        sim.ApplyServerOperation(sim.PreparePlayerJoin(new Horyzont.Core.Ids.PlatformIdentity(Horyzont.Core.Ids.IdentityPlatform.Dev, 1), "a"));
        var player = sim.State.Players.Online.Single();
        var feet = new Vector3(x, y, z);
        var rotation = SurfaceFrame.FromYaw(TerrainMath.UpAt(feet), 0.8f);
        Assert.True(sim.UpdatePlayerKinematics(player.Player, player.Frame, player.ParentSeq, feet, rotation, 0.3f));

        // The camera's up is deliberately not the body's up: the eye must not follow it.
        var pose = CameraRigMath.Compute(feet, rotation, 0.3f, Vector3.Normalize(new Vector3(1f, 1f, 1f)), CameraMode.FirstPerson, TppOffsets.Default, _rules);

        Assert.Equal(0f, Vector3.Distance(pose.Origin, player.EyePosition(_rules)));
    }

    [Fact]
    public void CameraRigMath_FirstPerson_DirectionFollowsYawAndPitch()
    {
        var up = Vector3.UnitX;
        var feet = up * 4000f;
        var rotation = SurfaceFrame.FromYaw(up, 0.6f);
        const float pitch = 0.4f;

        var pose = CameraRigMath.Compute(feet, rotation, pitch, up, CameraMode.FirstPerson, TppOffsets.Default, _rules);

        var forward = Vector3.Transform(-Vector3.UnitZ, rotation);
        var expected = (forward * MathF.Cos(pitch)) + (up * MathF.Sin(pitch));
        Assert.True((pose.Direction - expected).Length() < 1e-5f);
        Assert.Equal(1f, pose.Direction.Length(), 1e-5f);
    }

    [Fact]
    public void CameraRigMath_ThirdPerson_BehindRightShoulder_LooksAlongView()
    {
        var up = Vector3.Normalize(new Vector3(1f, 0.3f, -0.2f));
        var feet = up * 4000f;
        var rotation = SurfaceFrame.FromYaw(up, -1.1f);
        var offsets = TppOffsets.Default;

        foreach (var pitch in new[] { 0f, 0.4f, -0.7f })
        {
            var third = CameraRigMath.Compute(feet, rotation, pitch, up, CameraMode.ThirdPerson, offsets, _rules);
            var first = CameraRigMath.Compute(feet, rotation, pitch, up, CameraMode.FirstPerson, offsets, _rules);

            // Same view direction as in first person; the origin is displaced in the camera basis.
            Assert.True((third.Direction - first.Direction).Length() < 1e-6f);
            var right = Vector3.Normalize(Vector3.Cross(third.Direction, up));
            var cameraUp = Vector3.Cross(right, third.Direction);
            var shift = third.Origin - first.Origin;
            Assert.Equal(-3.5f, Vector3.Dot(shift, third.Direction), 1e-3f);
            Assert.Equal(0.5f, Vector3.Dot(shift, right), 1e-3f);
            Assert.Equal(0.2f, Vector3.Dot(shift, cameraUp), 1e-3f);
        }
    }

    [Fact]
    public void CameraRigMath_ThirdPerson_LevelView_ShiftIsInTheBodyBasis()
    {
        var up = Vector3.UnitX;
        var feet = up * 4000f;
        var rotation = SurfaceFrame.FromYaw(up, 0.9f);

        var third = CameraRigMath.Compute(feet, rotation, 0f, up, CameraMode.ThirdPerson, TppOffsets.Default, _rules);
        var first = CameraRigMath.Compute(feet, rotation, 0f, up, CameraMode.FirstPerson, TppOffsets.Default, _rules);

        var shift = Vector3.Transform(third.Origin - first.Origin, Quaternion.Inverse(rotation));
        Assert.True((shift - new Vector3(0.5f, 0.2f, 3.5f)).Length() < 1e-4f, $"shift in the body basis: {shift}");
    }

    [Fact]
    public void CameraRigMath_DevFree_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CameraRigMath.Compute(Vector3.UnitX * 4000f, Quaternion.Identity, 0f, Vector3.UnitX, CameraMode.DevFree, TppOffsets.Default, _rules));

    [Fact]
    public void CameraRigMath_Steady_DoesNotAllocate()
    {
        var feet = Vector3.UnitX * 4000f;
        var rotation = SurfaceFrame.FromYaw(Vector3.UnitX, 0.3f);
        for (var i = 0; i < 100; i++)
        {
            CameraRigMath.Compute(feet, rotation, 0.1f, Vector3.UnitX, CameraMode.ThirdPerson, TppOffsets.Default, _rules);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            CameraRigMath.Compute(feet, rotation, 0.1f, Vector3.UnitX, i % 2 == 0 ? CameraMode.ThirdPerson : CameraMode.FirstPerson, TppOffsets.Default, _rules);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
