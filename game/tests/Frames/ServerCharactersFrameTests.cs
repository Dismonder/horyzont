using System.Numerics;

using GdUnit4;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Frames;
using Horyzont.Game.Physics;
using Horyzont.Game.Tests.Character.Fixtures;
using Horyzont.Game.Tests.Support;
using Horyzont.Game.Tests.Terrain;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Frames;

/// <summary>K5-20 step 13 (decision 15, review K5 B2): the server body follows every reparent of its player.</summary>
[TestSuite]
public class ServerCharactersFrameTests
{
    private static readonly PlanetParams _other = TerrainTestWorld.Planet with { Id = new PlanetId(2) };

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task ServerCharacters_PlayerInConstructFrame_AttachedWithoutCollisionAndFollowsTheFrame()
    {
        await using var scene = EngineTestScene.Create();
        await using var rig = await CharacterRig.CreateAsync(scene);
        var feet = TerrainTestWorld.Ground(0, 0) + new Vector3(0, 0.05f, 0);
        var player = rig.Join(1, feet);
        AssertThat(await rig.WaitUnfrozenAsync(player)).IsTrue();

        // A construct 20 m above the player; the player is put into its frame with a pose 1 m above its origin.
        var construct = rig.Sim.State.EntityIds.PeekNext();
        var origin = feet + new Vector3(0, 20, 0);
        rig.Sim.ApplyServerOperation(new ConstructCreateOp(
            construct, FrameRef.Of(TerrainTestWorld.Planet.Id), origin, Quaternion.Identity, new CellPos(0, 0, 0), BlockState.Create(1, 0)));
        rig.Sim.State.Players.TryGet(player, out var state);
        var local = new Vector3(0.5f, 1f, 0.5f);
        rig.Sim.ApplyServerOperation(new EntityReparentOp(
            state!.Entity, FrameRef.Of(construct), state.ParentSeq + 1, local, Quaternion.Identity, Vector3.Zero, null));
        AssertThat(await rig.WaitTicks(rig.Ticks + 3)).IsTrue();

        var body = rig.BodyOf(player);
        AssertThat(body.Attached).IsTrue();
        AssertThat(body.CollisionLayer).IsEqual(0u);
        AssertThat(body.CollisionMask).IsEqual(0u);
        AssertThat((double)body.GlobalPosition.DistanceTo(GodotFrames.ToGodot(Vector3D.FromFloat(origin + local)))).IsLess(1e-3);

        // The replicated state is the local pose in the construct frame; the simulation keeps it (no kinematics are written).
        AssertThat(rig.Characters.TryGetState(player, out var replicated)).IsTrue();
        AssertThat(replicated.Frame).IsEqual(FrameRef.Of(construct));
        AssertThat(replicated.Feet).IsEqual(local);
        rig.Sim.State.Players.TryGet(player, out state);
        AssertThat(state!.Position).IsEqual(local);

        // Back to the planet: the body walks again from the pose of the operation, with its collision.
        rig.Sim.ApplyServerOperation(new EntityReparentOp(
            state.Entity, FrameRef.Of(TerrainTestWorld.Planet.Id), state.ParentSeq + 1, feet, state.BodyRotation, Vector3.Zero, null));
        AssertThat(await rig.WaitTicks(rig.Ticks + 2)).IsTrue();
        AssertThat(body.Attached).IsFalse();
        AssertThat(body.CollisionLayer).IsEqual(1u << (CollisionLayers.ServerBodies - 1));
        AssertThat(await rig.WaitUnfrozenAsync(player)).IsTrue();
        AssertThat(Vector3.Distance(body.Feet, feet)).IsLess(0.2f);
    }

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task ServerCharacters_ReparentToOtherPlanet_BodyLeavesTheOldPlanetAndSpawnsOnTheNewOne()
    {
        await using var scene = EngineTestScene.Create();
        await using var rig = await CharacterRig.CreateAsync(scene, morePlanets: [_other]);
        var feet = TerrainTestWorld.Ground(0, 0) + new Vector3(0, 0.05f, 0);
        var player = rig.Join(1, feet);
        AssertThat(await rig.WaitUnfrozenAsync(player)).IsTrue();

        rig.Sim.State.Players.TryGet(player, out var state);
        rig.Sim.ApplyServerOperation(new EntityReparentOp(
            state!.Entity, FrameRef.Of(_other.Id), state.ParentSeq + 1, feet, state.BodyRotation, Vector3.Zero, null));
        AssertThat(await rig.WaitTicks(rig.Ticks + 2)).IsTrue();

        // The other planet has no terrain in this server (K5-22 activates it): the normal spawn path refuses, the old body is gone.
        AssertThat(rig.Characters.TryGetState(player, out _)).IsFalse();
        AssertThat(rig.Characters.Count).IsEqual(0);
        AssertThat(rig.Log.Count(9310)).IsEqual(1);
        AssertThat(rig.Log.Count(9303)).IsEqual(1);
        AssertThat(rig.Characters.GetNodeOrNull($"Body_{player.Value}") is null || rig.Characters.GetNode($"Body_{player.Value}").IsQueuedForDeletion()).IsTrue();

        // The refused spawn is retried every tick (logged once): back on a planet with terrain, the body exists again.
        AssertThat(rig.Characters.PendingCount).IsEqual(1);
        AssertThat(await rig.WaitTicks(rig.Ticks + 3)).IsTrue();
        AssertThat(rig.Log.Count(9303)).IsEqual(1);
        rig.Sim.State.Players.TryGet(player, out state);
        rig.Sim.ApplyServerOperation(new EntityReparentOp(
            state!.Entity, FrameRef.Of(TerrainTestWorld.Planet.Id), state.ParentSeq + 1, feet, state.BodyRotation, Vector3.Zero, null));
        AssertThat(await rig.WaitTicks(rig.Ticks + 2)).IsTrue();
        AssertThat(rig.Characters.Count).IsEqual(1);
        AssertThat(rig.Characters.PendingCount).IsEqual(0);
        AssertThat(rig.Characters.TryGetState(player, out var back)).IsTrue();
        AssertThat(back.Planet).IsEqual(TerrainTestWorld.Planet.Id);
        AssertThat(await rig.WaitUnfrozenAsync(player)).IsTrue();
    }

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task ServerCharacters_SpawnInShipInSystemFrame_AttachedOnTheLastPlanet()
    {
        await using var scene = EngineTestScene.Create();
        await using var rig = await CharacterRig.CreateAsync(scene);
        var feet = TerrainTestWorld.Ground(0, 0) + new Vector3(0, 0.05f, 0);
        var player = rig.Join(1, feet);
        AssertThat(await rig.WaitUnfrozenAsync(player)).IsTrue();

        // A construct moved into the system frame (a ship between planets, home planet in identity), the player inside it.
        var construct = rig.Sim.State.EntityIds.PeekNext();
        var origin = feet + new Vector3(0, 20, 0);
        rig.Sim.ApplyServerOperation(new ConstructCreateOp(
            construct, FrameRef.Of(TerrainTestWorld.Planet.Id), origin, Quaternion.Identity, new CellPos(0, 0, 0), BlockState.Create(1, 0)));
        rig.Sim.ApplyServerOperation(new EntityReparentOp(construct, FrameRef.Of(new SystemId(1)), 1, origin, Quaternion.Identity, Vector3.Zero, null));
        rig.Sim.State.Players.TryGet(player, out var state);
        var local = new Vector3(0.5f, 1f, 0.5f);
        rig.Sim.ApplyServerOperation(new EntityReparentOp(
            state!.Entity, FrameRef.Of(construct), state.ParentSeq + 1, local, Quaternion.Identity, Vector3.Zero, null));
        AssertThat(await rig.WaitTicks(rig.Ticks + 2)).IsTrue();
        AssertThat(rig.BodyOf(player).Attached).IsTrue();

        // A new body (reconnect) is spawned attached on the last planet instead of being refused.
        rig.Characters.Despawn(player);
        rig.Characters.Spawn(player);
        AssertThat(rig.Characters.Count).IsEqual(1);
        AssertThat(rig.Characters.PendingCount).IsEqual(0);
        var body = rig.BodyOf(player);
        AssertThat(body.Attached).IsTrue();
        AssertThat(await rig.WaitTicks(rig.Ticks + 2)).IsTrue();
        AssertThat((double)body.GlobalPosition.DistanceTo(GodotFrames.ToGodot(Vector3D.FromFloat(origin + local)))).IsLess(1e-3);
    }
}
