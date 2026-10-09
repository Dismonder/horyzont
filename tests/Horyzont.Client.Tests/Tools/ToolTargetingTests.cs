using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Client.Tests.Tools;

public sealed class ToolTargetingTests
{
    private static readonly Vector3 _feet = new(0f, 4000.1f, 0f);

    private static AimRay Aim(FakeToolWorldView world, Vector3 direction) =>
        new(FakeToolWorldView.Planet, AimRayBuilder.EyeFromFeet(_feet, world.Rules), direction, -Vector3.UnitZ, _feet);

    private static TerrainHit Terrain(float distance) => new(new Vector3(0f, 4000f, 0f), Vector3.UnitY, distance);

    private static ConstructHit Construct(float distance) => new(new EntityId(9), new CellPos(1, 2, 3), Face.PosX, new Vector3(1f, 2f, 3f), distance);

    [Fact]
    public void ToolTargeting_ConstructStrictlyCloser_TargetsConstructElseTerrain()
    {
        var world = new FakeToolWorldView { TerrainOverride = Terrain(6f), ConstructOverride = Construct(5f) };
        var aim = Aim(world, -Vector3.UnitZ);

        Assert.True(ToolTargeting.TryTarget(aim, world, world.Rules, out var closer));
        Assert.Equal(ToolTargetKind.ConstructCell, closer.Kind);
        Assert.Equal(Face.PosX, closer.Face);
        Assert.Equal(new CellPos(1, 2, 3), closer.Cell);
        Assert.Equal(new EntityId(9), closer.Construct);
        Assert.Equal(5f, closer.Distance);

        world.ConstructOverride = Construct(6f);
        Assert.True(ToolTargeting.TryTarget(aim, world, world.Rules, out var tie));
        Assert.Equal(ToolTargetKind.Terrain, tie.Kind);

        world.ConstructOverride = Construct(7f);
        Assert.True(ToolTargeting.TryTarget(aim, world, world.Rules, out var farther));
        Assert.Equal(ToolTargetKind.Terrain, farther.Kind);
    }

    [Fact]
    public void ToolTargeting_ConstructOnlyWithinReach_TargetsConstruct()
    {
        var world = new FakeToolWorldView { ConstructOverride = Construct(7.9f) };

        Assert.True(ToolTargeting.TryTarget(Aim(world, -Vector3.UnitZ), world, world.Rules, out var target));
        Assert.Equal(ToolTargetKind.ConstructCell, target.Kind);
    }

    [Theory]
    [InlineData(8f, true)]
    [InlineData(8.01f, false)]
    public void ToolTargeting_HitBeyondReach_NoTarget(float distance, bool expected)
    {
        var world = new FakeToolWorldView { TerrainOverride = Terrain(distance) };
        var aim = Aim(world, -Vector3.UnitZ);

        // The override ignores the maximum distance of the ray the way a lenient adapter might; the targeting checks the reach itself.
        var lenient = new LenientView(world);

        Assert.Equal(expected, ToolTargeting.TryTarget(aim, lenient, world.Rules, out var target));
        Assert.Equal(expected ? ToolTargetKind.Terrain : ToolTargetKind.None, target.Kind);
        Assert.Equal(expected, ToolTargeting.TryTargetTerrain(aim, lenient, world.Rules, out _, out _));
    }

    [Fact]
    public void ToolTargeting_RealConstructInFrontOfTerrain_TargetsItsFace()
    {
        var world = new FakeToolWorldView();
        var eye = AimRayBuilder.EyeFromFeet(_feet, world.Rules);
        world.AddConstruct(new Vector3(-0.5f, eye.Y - 0.5f, -4.5f));

        Assert.True(ToolTargeting.TryTarget(Aim(world, -Vector3.UnitZ), world, world.Rules, out var target));

        Assert.Equal(ToolTargetKind.ConstructCell, target.Kind);
        Assert.Equal(Face.PosZ, target.Face);
        Assert.Equal(3.5f, target.Distance, 1e-4f);
        Assert.Equal(Vector3.UnitZ, target.Normal);
    }

    [Fact]
    public void ToolTargeting_TerrainBehindConstruct_ManipulatorIsBlocked()
    {
        var world = new FakeToolWorldView();
        var eye = AimRayBuilder.EyeFromFeet(_feet, world.Rules);
        world.AddConstruct(new Vector3(0f, eye.Y - 1.6f, -0.5f) + new Vector3(-0.5f, 0f, -1f));
        var towardGround = Vector3.Normalize(world.Ground(0f, -3f) - eye);

        Assert.True(ToolTargeting.TryTargetTerrain(Aim(world, towardGround), world, world.Rules, out var target, out var blocked));

        Assert.Equal(ToolTargetKind.Terrain, target.Kind);
        Assert.True(blocked);
    }

    /// <summary>Returns forced hits whatever the maximum distance is.</summary>
    private sealed class LenientView(FakeToolWorldView inner) : IToolWorldView
    {
        public bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out Horyzont.Core.World.PlanetParams? parameters) => inner.TryGetPlanet(planet, out parameters);

        public bool RaycastTerrain(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out TerrainHit hit) =>
            inner.RaycastTerrain(planet, origin, direction, float.MaxValue, out hit);

        public bool RaycastConstructs(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out ConstructHit hit) =>
            inner.RaycastConstructs(planet, origin, direction, float.MaxValue, out hit);

        public bool TryGetConstruct(EntityId construct, [NotNullWhen(true)] out Construct? value) => inner.TryGetConstruct(construct, out value);

        public bool TryGetConstructPose(EntityId construct, out Vector3 origin, out Quaternion rotation) =>
            inner.TryGetConstructPose(construct, out origin, out rotation);

        public bool OverlapsConstruct(PlanetId planet, Vector3 center, Quaternion rotation, EntityId? skip) =>
            inner.OverlapsConstruct(planet, center, rotation, skip);

        public int CopyOtherPlayerFeet(PlanetId planet, Span<Vector3> destination) => inner.CopyOtherPlayerFeet(planet, destination);
    }
}
