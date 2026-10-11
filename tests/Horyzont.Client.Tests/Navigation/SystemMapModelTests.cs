using System.Numerics;

using Horyzont.Client.Frames;
using Horyzont.Client.Markers;
using Horyzont.Client.Navigation;
using Horyzont.Client.Replication;
using Horyzont.Client.Space;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Space;
using Horyzont.Core.World.Markers;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Navigation;

public sealed class SystemMapModelTests
{
    private sealed class FixedViewpoint(Vector3D? position) : INavigationViewpoint
    {
        public bool TryGetWorldPosition(double renderTick, out Vector3D result)
        {
            result = position ?? default;
            return position is not null;
        }
    }

    [Fact]
    public void SystemMapModel_ContainsBodiesSunEntitiesMarkers()
    {
        var created = StarSystemGenesis.CreateWorldBodies(11, "core:test/1");
        var hello = new ServerHello(default, new Tick(0), 60, new PlayerId(1), new EntityId(1), created.Bodies, [], []);
        var time = new FakeTimeProvider();
        var entities = new EntityMirror(time);
        var constructs = new ConstructMirror(time);
        var stars = new ClientStarSystem();
        var frames = new ClientFrames(entities, constructs, stars);
        stars.OnServerHello(hello);
        frames.OnServerHello(hello);
        stars.OnMessage(new StarSystemState(created.System));
        var home = created.Bodies[0];
        var homeFrame = FrameRef.Of(home.Id);

        // A ship and a remote player near the home planet.
        var ship = new EntityId(5);
        var friend = new EntityId(6);
        entities.Apply(new EntitySpawn(ship, EntityKind.Construct, 0, new PlayerId(2), new SnapshotFrame(homeFrame, 0), State(ship, new Vector3(0, 4010, 0)), []));
        entities.Apply(new EntitySpawn(friend, EntityKind.Player, 0, new PlayerId(3), new SnapshotFrame(homeFrame, 0), State(friend, new Vector3(5, 4000, 0)), []));
        var markers = new ClientMarkerStore();
        markers.OnMessage(new MarkersState(
            [new MarkerRecord(new MarkerId(1), null, created.Bodies[1].Id, new Vector3(0, 1800, 0), "Moon base", MarkerIcon.Base, MarkerColor.Green)]));

        var model = SystemMapModel.Build(
            stars, new ClientFramePoses(frames.Graph!), markers, entities, new FixedViewpoint(new Vector3D(1, 2, 3)), 120, 60, 2,
            (_, owner, kind) => kind == EntityKind.Construct ? "Ship of P" + owner!.Value.Value : "Player " + owner!.Value.Value);

        Assert.Equal(4, model.Bodies.Count);
        Assert.Equal(["BODY_HOME", "BODY_MOON", "BODY_B", "BODY_C"], model.Bodies.Select(b => b.NameKey));
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(created.System.Bodies[i].Planet, model.Bodies[i].Planet);
            Assert.Equal(created.System.Bodies[i].Pose.Position.X, model.Bodies[i].X);
            Assert.Equal(created.System.Bodies[i].Pose.Position.Z, model.Bodies[i].Z);
            Assert.Equal(StarSystemRules.ZoneRadiusFactor * created.Bodies[i].RadiusMeters, model.Bodies[i].ZoneRadiusMeters);
        }

        Assert.Equal(StarSystemMath.SunDirection(120, 60, created.System.SunCycleSeconds), model.SunDirection);
        Assert.Equal(["Ship of P2", "Player 3"], model.Entities.OrderBy(e => e.Entity.Value).Select(e => e.Name));
        var shipEntry = model.Entities.Single(e => e.Entity == ship);
        Assert.Equal(created.System.Bodies[0].Pose.Position.Y + 4010, shipEntry.Y, 3);
        var marker = Assert.Single(model.Markers);
        Assert.Equal("Moon base", marker.Name);
        Assert.True(marker.IsShared);
        Assert.Equal(new Vector3D(1, 2, 3), model.OwnPosition);
        Assert.Null(SystemMapModel.Build(stars, new ClientFramePoses(frames.Graph!), markers, entities, new FixedViewpoint(null), 0, 60, 0).OwnPosition);
    }

    private static EntityState State(EntityId id, Vector3 position) => new(id, (EntityStateFields)15, 0, 0, position, Quaternion.Identity, Vector3.Zero, 0);
}
