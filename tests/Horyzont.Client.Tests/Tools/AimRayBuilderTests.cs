using System.Numerics;

using Horyzont.Client.Tools;
using Horyzont.Core.Simulation;

namespace Horyzont.Client.Tests.Tools;

public sealed class AimRayBuilderTests
{
    private static readonly Vector3 _feet = new(0f, 4000.1f, 0f);

    [Fact]
    public void AimRayBuilder_ThirdPerson_RayStartsAtEyeTowardCrosshairHit()
    {
        var world = new FakeToolWorldView();
        var player = world.Join();
        world.MovePlayer(player, _feet);
        var serverEye = world.ServerEye(player);

        // A block 5.5 m in front of the player; the camera is 4 m behind and 1 m above the eye and looks at its front face.
        world.AddConstruct(new Vector3(-0.5f, serverEye.Y - 0.72f, -6.5f));
        var camera = serverEye + new Vector3(0f, 1f, 4f);
        var cameraDirection = Vector3.Normalize(new Vector3(0f, -1f, -9.5f));

        Assert.True(AimRayBuilder.TryBuild(FakeToolWorldView.Planet, _feet, camera, cameraDirection, -Vector3.UnitZ, world, world.Rules, out var ray));

        Assert.Equal(0f, Vector3.Distance(ray.Eye, serverEye));
        Assert.Equal(FakeToolWorldView.Planet, ray.Planet);
        Assert.Equal(0f, ray.Direction.X, 1e-4f);
        Assert.Equal(0f, ray.Direction.Y, 1e-4f);
        Assert.Equal(-1f, ray.Direction.Z, 1e-4f);
        Assert.Equal(1f, ray.Direction.Length(), 1e-5f);
    }

    [Fact]
    public void AimRayBuilder_ThirdPersonNothingHit_AimsAtPointAtReachBeyondCamera()
    {
        var world = new FakeToolWorldView();
        var eye = AimRayBuilder.EyeFromFeet(_feet, world.Rules);
        var camera = eye + new Vector3(0f, 0f, 4f);

        Assert.True(AimRayBuilder.TryBuild(FakeToolWorldView.Planet, _feet, camera, -Vector3.UnitZ, -Vector3.UnitZ, world, world.Rules, out var ray));

        Assert.Equal(-1f, ray.Direction.Z, 1e-5f);
    }

    [Fact]
    public void AimRayBuilder_FirstPerson_RayIsTheCameraRay()
    {
        var world = new FakeToolWorldView();
        var eye = AimRayBuilder.EyeFromFeet(_feet, world.Rules);
        var direction = Vector3.Normalize(new Vector3(0.3f, -0.2f, -1f));

        Assert.True(AimRayBuilder.TryBuild(FakeToolWorldView.Planet, _feet, eye, direction, -Vector3.UnitZ, world, world.Rules, out var ray));

        Assert.Equal(eye, ray.Eye);
        Assert.Equal(0f, Vector3.Distance(direction, ray.Direction), 1e-6f);
        Assert.Equal(-Vector3.UnitZ, ray.LookForward);
        Assert.Equal(_feet, ray.Feet);
    }

    [Fact]
    public void EyeFromFeet_EqualsTheServerFormula()
    {
        var world = new FakeToolWorldView();
        var player = world.Join();
        var feet = new Vector3(120f, 3998f, -45f);
        world.MovePlayer(player, feet);

        Assert.Equal(world.ServerEye(player), AimRayBuilder.EyeFromFeet(feet, new GameRules()));
    }

    [Theory]
    [InlineData(float.NaN, 0f, -1f)]
    [InlineData(0f, float.PositiveInfinity, -1f)]
    [InlineData(0f, 0f, 0f)]
    [InlineData(0f, 0f, 1e-6f)]
    public void AimRayBuilder_NonFiniteOrDegenerateCameraDirection_ReturnsFalse(float x, float y, float z)
    {
        var world = new FakeToolWorldView();
        var eye = AimRayBuilder.EyeFromFeet(_feet, world.Rules);

        Assert.False(AimRayBuilder.TryBuild(FakeToolWorldView.Planet, _feet, eye, new Vector3(x, y, z), -Vector3.UnitZ, world, world.Rules, out _));
    }

    [Fact]
    public void AimRayBuilder_NonFinitePositions_ReturnsFalse()
    {
        var world = new FakeToolWorldView();
        var eye = AimRayBuilder.EyeFromFeet(_feet, world.Rules);
        var nan = new Vector3(float.NaN, 0f, 0f);

        Assert.False(AimRayBuilder.TryBuild(FakeToolWorldView.Planet, nan, eye, -Vector3.UnitZ, -Vector3.UnitZ, world, world.Rules, out _));
        Assert.False(AimRayBuilder.TryBuild(FakeToolWorldView.Planet, _feet, nan, -Vector3.UnitZ, -Vector3.UnitZ, world, world.Rules, out _));
        Assert.False(AimRayBuilder.TryBuild(FakeToolWorldView.Planet, _feet, eye, -Vector3.UnitZ, nan, world, world.Rules, out _));
    }

    [Fact]
    public void AimRayBuilder_ZeroBodyDirection_FallsBackToTheCameraDirection()
    {
        var world = new FakeToolWorldView();
        var eye = AimRayBuilder.EyeFromFeet(_feet, world.Rules);

        Assert.True(AimRayBuilder.TryBuild(FakeToolWorldView.Planet, _feet, eye, -Vector3.UnitZ, Vector3.Zero, world, world.Rules, out var ray));
        Assert.Equal(-Vector3.UnitZ, ray.LookForward);
    }
}
