using GdUnit4;

using Godot;

using Horyzont.Client.Markers;
using Horyzont.Client.Navigation;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Markers;
using Horyzont.Game.Client.Navigation;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Navigation;

/// <summary>Engine tests of the navigation HUD (K5-27). Run one at a time on the runner (never in parallel).</summary>
[TestSuite]
public class NavigationHudTests
{
    private sealed class IdentityPoses : IWorldPoseSource
    {
        public bool TryGetWorldPose(FrameRef frame, double renderTick, out PoseD pose)
        {
            pose = PoseD.Identity;
            return true;
        }
    }

    private sealed class OriginViewpoint : INavigationViewpoint
    {
        public bool TryGetWorldPosition(double renderTick, out Vector3D position)
        {
            position = Vector3D.Zero;
            return true;
        }
    }

    private static (NavigationHud Hud, NavigationCoordinator Navigation) Build(EngineTestScene scene, System.Numerics.Vector3 markerAt)
    {
        var markers = new ClientMarkerStore();
        markers.OnMessage(new MarkersState([new MarkerRecord(new MarkerId(1), null, new PlanetId(1), markerAt, "Camp", MarkerIcon.Base, MarkerColor.Green)]));
        var navigation = new NavigationCoordinator(new IdentityPoses(), markers, null, new OriginViewpoint());
        navigation.Select(NavigationTarget.ForMarker(new MarkerId(1)));
        var camera = new Camera3D { Current = true, Fov = 75f };
        scene.Add(camera);
        var hud = scene.Add(new NavigationHud(navigation, markers, () => 0, ','));
        return (hud, navigation);
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task NavigationHud_TargetInView_Pin_Behind_EdgeArrow()
    {
        await using var scene = EngineTestScene.Create();
        var (hud, navigation) = Build(scene, new System.Numerics.Vector3(0f, 0f, -250f));
        await scene.WaitFrames(3);

        // In front of the camera (it looks along -Z): a pin at the centre, with the name and the distance.
        AssertThat(hud.PinVisible).IsTrue();
        AssertThat(hud.ArrowVisible).IsFalse();
        AssertThat(hud.PinText.StartsWith("Camp ", StringComparison.Ordinal)).IsTrue();
        AssertThat(hud.PinText.EndsWith(" m", StringComparison.Ordinal)).IsTrue();

        // Behind the camera: no pin, an arrow inside the 32 px margin.
        var markers = new ClientMarkerStore();
        markers.OnMessage(new MarkersState([new MarkerRecord(new MarkerId(2), null, new PlanetId(1), new System.Numerics.Vector3(0f, 0f, 400f), "Back", MarkerIcon.Base, MarkerColor.Red)]));
        var behind = new NavigationCoordinator(new IdentityPoses(), markers, null, new OriginViewpoint());
        behind.Select(NavigationTarget.ForMarker(new MarkerId(2)));
        var second = scene.Add(new NavigationHud(behind, markers, () => 0, '.'));
        await scene.WaitFrames(3);

        AssertThat(second.PinVisible).IsFalse();
        AssertThat(second.ArrowVisible).IsTrue();
        var view = second.GetViewport().GetVisibleRect().Size;
        var at = second.ScreenPosition;
        AssertThat(at.X >= 20.0 && at.X <= view.X - 20.0 && at.Y >= 20.0 && at.Y <= view.Y - 20.0).IsTrue();

        // Clearing the target hides everything.
        navigation.Clear();
        await scene.WaitFrames(2);
        AssertThat(hud.PinVisible || hud.ArrowVisible).IsFalse();
    }
}
