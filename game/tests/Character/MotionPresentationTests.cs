using GdUnit4;

using Godot;

using Horyzont.Client.Movement;
using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Game.Character;
using Horyzont.Game.Client.Character;
using Horyzont.Game.Frames;
using Horyzont.Game.Physics;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

using NumQuaternion = System.Numerics.Quaternion;
using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Character;

/// <summary>Real physics/world queries require the engine clock. Slow, bounded at 60 seconds; NOT RUN locally.</summary>
[TestSuite]
public class MotionPresentationTests
{
    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Host_PerfectLink_HasNoSpuriousCorrections()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var fixture = await ModeFixture.StartWithCharactersAsync(scene, dir, true, true,
            "--solo", "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", Guid.NewGuid().ToString());
        AssertThat(await fixture.WaitInGame()).IsTrue();
        var player = fixture.Composition.LocalPlayer!;
        AssertThat(await fixture.WaitUntil(() => player.IsActive, 180)).IsTrue();
        AssertThat(await fixture.WaitTerrainIdle()).IsTrue();
        player.ResetStatistics();
        var start = player.ClientTick;
        AssertThat(await fixture.WaitUntil(() => player.ClientTick >= start + 180, 30)).IsTrue();
        AssertThat(player.Corrections).IsEqual(0);

        // Inject a small complete authoritative correction for the newest recorded input.
        var hello = fixture.Composition.Client!.Hello!;
        fixture.Composition.ServerNode!.SetPhysicsProcess(false);
        player.SetPhysicsProcess(false);
        var before = player.PredictedFeet;
        var eyeBefore = player.Eye;
        var snapshot = new Snapshot(2_000_000, new Tick(2_000_000), 0, player.ClientTick,
            [new SnapshotFrame(FrameRef.Of(player.Planet), 0)],
            [new EntityState(hello.YourEntity, (EntityStateFields)15, 0, 0, before + NumVector3.UnitZ * 0.2f,
                player.BodyRotation, NumVector3.Zero, (byte)MovementMode.Walking)]);
        player.OnFullSnapshot(snapshot);
        player._PhysicsProcess(1d / 60);
        AssertThat(player.Motion.SmallCorrections).IsEqual(1L);
        AssertThat(NumVector3.Distance(before, player.PredictedFeet)).IsGreater(0.1f);
        AssertThat(NumVector3.Distance(eyeBefore, player.Eye)).IsGreater(0.1f);
        player._Process(0.1);
        player.OnDisconnected(Horyzont.Core.Protocol.DisconnectCode.Normal);
        AssertThat(player.PredictionPaused).IsFalse();
        fixture.Composition.ServerNode.SetPhysicsProcess(true);
        AssertThat(await fixture.StopAsync()).IsTrue();
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Reconcile_SmallError_VisualMovesWithoutMovingCapsule()
    {
        await using var scene = EngineTestScene.Create();
        var planet = new PlanetParams(new PlanetId(1), new SystemId(1), 1, "core:planet/1");
        var body = PlayerBody.Instantiate();
        body.Configure(planet, new GameRules(), new MovementRules(), false, CollisionLayers.ClientBodies, 1f / 60);
        body.PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Off;
        scene.Add(body);
        var feet = new NumVector3(0, 4000, 0);
        body.Place(feet, NumVector3.Zero, NumQuaternion.Identity, MovementMode.Walking, true);
        var before = new PredictedState(feet, NumVector3.Zero, NumQuaternion.Identity, 0, MovementMode.Walking, true);
        var after = before with { Feet = feet + NumVector3.UnitX * 0.2f };
        var correction = new PresentationCorrection();
        body.Place(after.Feet, after.Velocity, after.BodyRotation, after.Mode, true, false);
        correction.Reconcile(before, after, false);
        body.ApplyPresentation(correction.PositionOffset, correction.RotationOffset);
        var visual = body.GetNode<MeshInstance3D>("Visual");
        AssertThat(body.Feet).IsEqual(after.Feet);
        AssertThat(PlanetFrames.ToPlanetLocal(planet.Id, visual.GlobalPosition).X).IsEqual(feet.X);
        correction.Advance(0.1f);
        body.ApplyPresentation(correction.PositionOffset, correction.RotationOffset);
        AssertThat(PlanetFrames.ToPlanetLocal(planet.Id, visual.GlobalPosition).X).IsEqual(after.Feet.X);
        AssertThat(body.Feet).IsEqual(after.Feet);
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task UpdateCamera_CorrectionNearWall_DoesNotCrossWallOrChangeAimEye()
    {
        await using var scene = EngineTestScene.Create();
        var rig = scene.Add(new CameraRig());
        var wall = new StaticBody3D { CollisionLayer = CollisionLayers.ConstructsMask };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(20, 20, 0.1) } });
        wall.Position = new Vector3(0, 4001.6, 1);
        scene.Add(wall);
        await scene.WaitFrames(3);
        var planet = new PlanetId(1);
        var eye = new NumVector3(0, 4001.6f, 0);
        var wanted = eye + new NumVector3(0.2f, 0, 3);
        var resolved = rig.ResolvePresentation(planet, eye, wanted);
        AssertThat(resolved.Z).IsLess(1f);
        AssertThat(eye).IsEqual(new NumVector3(0, 4001.6f, 0));
        var fpp = rig.ResolvePresentation(planet, eye, eye + NumVector3.UnitZ * 1.2f);
        AssertThat(fpp.Z).IsLess(1f);
        // Pole-aligned viewing directions must produce a finite camera basis after smoothing.
        rig.Apply(planet, fpp, NumVector3.UnitY, NumVector3.UnitY, false);
        AssertThat(rig.Camera.GlobalBasis.IsFinite()).IsTrue();
        rig.ToggleBodyMode();
        AssertThat(rig.BodyMode).IsEqual(Horyzont.Client.CameraMode.FirstPerson);
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task RemotePresentation_FullSample_HasNoPhysicsBody()
    {
        await using var scene = EngineTestScene.Create();
        var view = scene.Add(new RemotePlayerPresentation());
        var sample = new RemoteMotionSample(new EntityId(2), FrameRef.Of(new PlanetId(1)), 0, new Tick(10),
            new NumVector3(0, 4000, 0), NumQuaternion.Identity, NumVector3.Zero, 1);
        view.SetSample(sample);
        AssertThat(view.GlobalPosition).IsEqual(PlanetFrames.ToGodot(new PlanetId(1), sample.Position));
        AssertThat((Node)view is PhysicsBody3D).IsFalse();
        foreach (var child in view.GetChildren())
        {
            AssertThat(child is CollisionObject3D).IsFalse();
        }
    }
}
