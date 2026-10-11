using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Server.Frames;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

namespace Horyzont.Server.Tests.Frames;

public sealed class CharacterFramesTests
{
    private static readonly PlanetId _home = WorldFactory.HomePlanet;
    private static readonly PlanetId _other = new(77);

    [Fact]
    public void CharacterFrames_SameParentSeq_None()
    {
        var rig = new FrameRig();
        Assert.Equal(CharacterFrameAction.None, CharacterFrames.Decide(rig.Graph, _home, 3, FrameRef.Of(TestShips.Moving), 3, out _));
    }

    [Fact]
    public void CharacterFrames_ConstructInTheBodyPlanet_Attach()
    {
        var rig = new FrameRig();
        Assert.Equal(CharacterFrameAction.Attach, CharacterFrames.Decide(rig.Graph, _home, 0, FrameRef.Of(TestShips.Moving), 1, out var planet));
        Assert.Equal(_home, planet);
    }

    [Fact]
    public void CharacterFrames_ConstructToPlanetWithVelocity_Glide()
    {
        var rig = new FrameRig();
        var eject = new Vector3(0, 0, 12);

        Assert.Equal(
            CharacterFrameAction.Glide,
            CharacterFrames.Decide(rig.Graph, _home, 1, FrameKind.Construct, FrameRef.Of(_home), 2, eject, out var planet));
        Assert.Equal(_home, planet);
        Assert.Equal(CharacterFrameAction.Walk, CharacterFrames.Decide(rig.Graph, _home, 1, FrameKind.Construct, FrameRef.Of(_home), 2, Vector3.Zero, out _));
        Assert.Equal(CharacterFrameAction.Walk, CharacterFrames.Decide(rig.Graph, _home, 1, FrameKind.Planet, FrameRef.Of(_home), 2, eject, out _));
        Assert.Equal(CharacterFrameAction.Respawn, CharacterFrames.Decide(rig.Graph, _home, 1, FrameKind.Construct, FrameRef.Of(_other), 2, eject, out _));
    }

    [Fact]
    public void CharacterFrames_BackToTheBodyPlanet_Walk()
    {
        var rig = new FrameRig();
        Assert.Equal(CharacterFrameAction.Walk, CharacterFrames.Decide(rig.Graph, _home, 1, FrameRef.Of(_home), 2, out var planet));
        Assert.Equal(_home, planet);
    }

    [Fact]
    public void CharacterFrames_ReparentToOtherPlanet_Respawn()
    {
        var rig = new FrameRig();
        Assert.Equal(CharacterFrameAction.Respawn, CharacterFrames.Decide(rig.Graph, _home, 1, FrameRef.Of(_other), 2, out var planet));
        Assert.Equal(_other, planet);
    }

    [Fact]
    public void CharacterFrames_ConstructInAnotherPlanet_RespawnAttached()
    {
        var rig = new FrameRig();
        Assert.Equal(CharacterFrameAction.Respawn, CharacterFrames.Decide(rig.Graph, _other, 1, FrameRef.Of(TestShips.Moving), 2, out var planet));
        Assert.Equal(_home, planet);
    }

    [Fact]
    public void CharacterFrames_SystemOrUnknownFrame_Unsupported()
    {
        var rig = new FrameRig();
        Assert.Equal(CharacterFrameAction.Unsupported, CharacterFrames.Decide(rig.Graph, _home, 1, FrameRef.Of(new SystemId(1)), 2, out _));
        Assert.Equal(CharacterFrameAction.Unsupported, CharacterFrames.Decide(rig.Graph, _home, 1, FrameRef.Of(new EntityId(999)), 2, out _));
    }

    [Fact]
    public void CharacterFrames_ConstructInSystemRegion_AttachOnTheBodyPlanet()
    {
        var rig = new FrameRig();
        rig.Tick(3);
        Assert.True(rig.Transitions.TryReparent(TestShips.Moving, FrameRef.Of(new SystemId(1)), null, out _, out var failure), failure.ToString());
        Assert.Equal(CharacterFrameAction.Attach, CharacterFrames.Decide(rig.Graph, _home, 0, FrameRef.Of(TestShips.Moving), 1, out var planet));
        Assert.Equal(_home, planet);
    }

    [Fact]
    public void CharacterFrames_SpawnPlanet_AgreesWithDecide()
    {
        var rig = new FrameRig();
        Assert.True(CharacterFrames.TrySpawnPlanet(rig.Graph, FrameRef.Of(_other), _home, out var planet, out var attached));
        Assert.Equal((_other, false), (planet, attached));
        Assert.True(CharacterFrames.TrySpawnPlanet(rig.Graph, FrameRef.Of(TestShips.Moving), _other, out planet, out attached));
        Assert.Equal((_home, true), (planet, attached));
        Assert.False(CharacterFrames.TrySpawnPlanet(rig.Graph, FrameRef.Of(new SystemId(1)), _home, out _, out _));
        Assert.False(CharacterFrames.TrySpawnPlanet(rig.Graph, FrameRef.Of(new EntityId(999)), _home, out _, out _));
    }

    [Fact]
    public void CharacterFrames_SpawnInShipInSystemRegion_AttachedOnTheFallbackPlanet()
    {
        // Decide attaches a body to a ship in the system frame on its planet; a spawn there (reconnect) uses the last/home planet.
        var rig = new FrameRig();
        rig.Tick(3);
        Assert.True(rig.Transitions.TryReparent(TestShips.Moving, FrameRef.Of(new SystemId(1)), null, out _, out var failure), failure.ToString());
        Assert.True(CharacterFrames.TrySpawnPlanet(rig.Graph, FrameRef.Of(TestShips.Moving), _other, out var planet, out var attached));
        Assert.Equal((_other, true), (planet, attached));
        Assert.False(CharacterFrames.TrySpawnPlanet(rig.Graph, FrameRef.Of(TestShips.Moving), default, out _, out _));
    }

    [Fact]
    public void CharacterFrames_PlayerInConstructFrame_WorldPoseFollowsTheShip()
    {
        var rig = new FrameRig();
        rig.Tick(5);
        var local = new Vector3(1f, 0.5f, -2f);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f);
        Assert.True(CharacterFrames.TryAttachedWorldPose(rig.Graph, FrameRef.Of(TestShips.Moving), local, rotation, out var first));
        rig.Tick(10);
        Assert.True(CharacterFrames.TryAttachedWorldPose(rig.Graph, FrameRef.Of(TestShips.Moving), local, rotation, out var second));

        var ship = rig.WorldPose(TestShips.Moving);
        var expected = FrameMath.Compose(ship, FrameMath.FromFloat(local, rotation));
        Assert.InRange(Vector3D.Distance(expected.Position, second.Position), 0, 1e-9);
        Assert.InRange(Math.Abs(QuaternionD.Dot(expected.Rotation, second.Rotation)), 1 - 1e-12, 1 + 1e-12);
        Assert.InRange(Vector3D.Distance(first.Position, second.Position), 1, 100);
        Assert.False(CharacterFrames.TryAttachedWorldPose(rig.Graph, FrameRef.Of(new EntityId(999)), local, rotation, out _));
    }
}
