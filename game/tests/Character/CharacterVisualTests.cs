using GdUnit4;

using Godot;

using Horyzont.Core.Motion;
using Horyzont.Core.Simulation;
using Horyzont.Game.Character;
using Horyzont.Game.Client.Character;
using Horyzont.Game.Physics;
using Horyzont.Game.Tests.Support;
using Horyzont.Game.Tests.Terrain;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Character;

[TestSuite]
public class CharacterVisualTests
{
    [TestCase]
    [RequireGodotRuntime]
    public void Choose_StandingWalkingRunningSprinting_PicksAnimationBySpeed()
    {
        AssertThat(CharacterVisual.Choose(MovementMode.Walking, 0.1, 0, 0).Animation.ToString()).IsEqual("Idle");
        AssertThat(CharacterVisual.Choose(MovementMode.Walking, 1.5, 0, 0).Animation.ToString()).IsEqual("Walk");
        AssertThat(CharacterVisual.Choose(MovementMode.Walking, 4.5, 0, 0).Animation.ToString()).IsEqual("Run");
        AssertThat(CharacterVisual.Choose(MovementMode.Sprinting, 7.5, 0, 0).Animation.ToString()).IsEqual("Sprint");
        AssertThat(CharacterVisual.Choose(MovementMode.Walking, 4.5, 0, 0).Speed).IsEqual(1f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Choose_InTheAir_JumpsThenFallsAfterTheDelay()
    {
        AssertThat(CharacterVisual.Choose(MovementMode.Jumping, 3, 4, 0).Animation.ToString()).IsEqual("Jump");
        // A short drop over a bump keeps the ground animation instead of flickering into the fall.
        AssertThat(CharacterVisual.Choose(MovementMode.Falling, 4.5, -1, 0.05).Animation.ToString()).IsEqual("Run");
        AssertThat(CharacterVisual.Choose(MovementMode.Falling, 0, -3, 0.5).Animation.ToString()).IsEqual("Fall");
        AssertThat(CharacterVisual.Choose(MovementMode.DevFlying, 20, 0, 1).Animation.ToString()).IsEqual("Fly");
        AssertThat(CharacterVisual.Choose(MovementMode.Frozen, 0, 0, 1).Animation.ToString()).IsEqual("Idle");
        AssertThat(CharacterVisual.JetBurns(MovementMode.DevFlying, 0)).IsTrue();
        AssertThat(CharacterVisual.JetBurns(MovementMode.Jumping, 3)).IsTrue();
        AssertThat(CharacterVisual.JetBurns(MovementMode.Walking, 0)).IsFalse();
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Attach_BodyInScene_AddsModelWithSkeletonAndAllAnimations()
    {
        await using var scene = EngineTestScene.Create();
        var body = PlayerBody.Instantiate();
        body.Configure(TerrainTestWorld.Planet, new GameRules(), new MovementRules(), false, CollisionLayers.ClientBodies, 1f / 60f);
        scene.Add(body);

        var visual = CharacterVisual.Attach(body);
        await scene.WaitFrames(2);

        AssertThat(visual.GetParent() == body.Visual).IsTrue();
        var skeleton = FindFirst<Skeleton3D>(visual);
        AssertThat(skeleton is not null).IsTrue();
        AssertThat(skeleton!.GetBoneCount()).IsGreaterEqual(25);
        var player = FindFirst<AnimationPlayer>(visual);
        AssertThat(player is not null).IsTrue();
        foreach (var name in new[] { "Idle", "Walk", "Run", "Sprint", "Jump", "Fall", "Fly" })
        {
            AssertThat(player!.HasAnimation(name)).OverrideFailureMessage($"missing animation {name}").IsTrue();
        }

        AssertThat(player!.GetAnimation("Walk").LoopMode).IsEqual(Animation.LoopModeEnum.Linear);
        AssertThat(visual.FindChild("Multitool", recursive: true, owned: false) is Node3D { Visible: true }).IsTrue();
        AssertThat(visual.FindChild("JetFlame", recursive: true, owned: false) is Node3D { Visible: false }).IsTrue();
        AssertThat(visual.CurrentAnimation?.ToString()).IsEqual("Idle");

        // The model is purely visual: the body keeps exactly its collision shape.
        AssertThat(body.GetChildCount()).IsEqual(2);
    }

    private static T? FindFirst<T>(Node root)
        where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T match)
            {
                return match;
            }

            if (FindFirst<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
