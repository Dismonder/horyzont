using System.Numerics;

using GdUnit4;

using Horyzont.Client.Frames;
using Horyzont.Client.Markers;
using Horyzont.Client.Navigation;
using Horyzont.Client.Replication;
using Horyzont.Client.Space;
using Horyzont.Client.UI;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Space;
using Horyzont.Core.World.Markers;
using Horyzont.Game.Client.Navigation;
using Horyzont.Game.Tests.Support;
using Horyzont.Game.UI.Map;

using Microsoft.Extensions.Time.Testing;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Navigation;

/// <summary>Engine tests of the map screens and of the map input gate (K5-27). Run one at a time on the runner (never in parallel).</summary>
[TestSuite]
public class MapPanelsTests
{
    private sealed class FixedGate(bool blocked) : IGameplayInputGate
    {
        public bool IsBlocked => blocked;
    }

    private sealed class NoViewpoint : INavigationViewpoint
    {
        public bool TryGetWorldPosition(double renderTick, out Horyzont.Core.Frames.Vector3D position)
        {
            position = new Horyzont.Core.Frames.Vector3D(10, 0, 10);
            return true;
        }
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task SystemMapPanel_RendersModel()
    {
        await using var scene = EngineTestScene.Create();
        var created = StarSystemGenesis.CreateWorldBodies(11, "core:test/1");
        var hello = new ServerHello(default, new Tick(0), 60, new PlayerId(1), new EntityId(1), created.Bodies, [], []);
        var time = new FakeTimeProvider();
        var entities = new EntityMirror(time);
        var stars = new ClientStarSystem();
        var frames = new ClientFrames(entities, new ConstructMirror(time), stars);
        stars.OnServerHello(hello);
        frames.OnServerHello(hello);
        stars.OnMessage(new StarSystemState(created.System));
        var markers = new ClientMarkerStore();
        markers.OnMessage(new MarkersState([
            new MarkerRecord(new MarkerId(1), null, created.Bodies[0].Id, new Vector3(0f, 4000f, 0f), "Home camp", MarkerIcon.Base, MarkerColor.Green),
            new MarkerRecord(new MarkerId(2), null, created.Bodies[1].Id, new Vector3(0f, 1800f, 0f), "Moon camp", MarkerIcon.Poi, MarkerColor.Red)]));

        var model = SystemMapModel.Build(stars, new LazyFramePoses(frames), markers, entities, new NoViewpoint(), 0, 60, 0);
        var panel = scene.Add(new SystemMapPanel { Size = new Godot.Vector2(1280, 720) });
        panel.SetModel(model);
        await scene.WaitFrames(3);

        AssertThat(panel.DrawnBodies == 4).IsTrue();
        AssertThat(panel.DrawnMarkers == 2).IsTrue();
        AssertThat(panel.DrewSun).IsTrue();
        AssertThat(panel.ToScreen(model.Bodies[0].X, model.Bodies[0].Z).X > 0.0).IsTrue();
    }

    [TestCase]
    public void MapInputGate_Open_BlocksGameplayInput()
    {
        var map = new MapInputGate();
        var combined = new CompositeInputGate(new FixedGate(false), map);

        AssertThat(combined.IsBlocked).IsFalse();
        map.IsOpen = true;
        AssertThat(combined.IsBlocked).IsTrue();
        map.IsOpen = false;
        AssertThat(new CompositeInputGate(new FixedGate(true), map).IsBlocked).IsTrue();
        AssertThat(new CompositeInputGate(null, map).IsBlocked).IsFalse();
    }
}
