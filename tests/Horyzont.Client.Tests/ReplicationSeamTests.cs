using System.Numerics;
using Horyzont.Client.Replication;
using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Testing;

namespace Horyzont.Client.Tests;

public sealed class ReplicationSeamTests
{
    [Fact]
    public void OnMessage_FullAndDelta_OnlyFullReachesEachSinkOnce()
    {
        var first = new Sink();
        var second = new Sink();
        var listener = new PassThroughFullSnapshots();
        listener.AddSink(first);
        listener.AddSink(second);
        var full = new Snapshot(1, new Tick(1), 0, 0, [], []);
        listener.OnMessage(full);
        listener.OnMessage(full with { SnapshotId = 2, BaselineSnapshotId = 1 });
        listener.OnMessage(new EntityDespawn(new EntityId(1)));
        Assert.Same(full, Assert.Single(first.Snapshots));
        Assert.Same(full, Assert.Single(second.Snapshots));
        Assert.True(AlwaysReadyWorld.Instance.IsReady);
    }

    [Fact]
    public void RemoteToolWorldView_HelloAndNeutralTerrain_ExposesPlanetsOnly()
    {
        var world = WorldFactory.CreateDefault();
        var hello = new ServerHello(world.Id, world.CurrentTick, 60, new PlayerId(1), new EntityId(1), world.Planets.All, [], []);
        var view = new RemoteToolWorldView(hello, NoRemoteTerrain.Instance);
        var planet = hello.Planets[0];
        Assert.True(view.TryGetPlanet(planet.Id, out var found));
        Assert.Same(planet, found);
        Assert.False(view.TryGetPlanet(default, out _));
        Assert.False(view.RaycastTerrain(planet.Id, Vector3.Zero, Vector3.UnitX, 10, out _));
        Assert.False(view.RaycastConstructs(planet.Id, Vector3.Zero, Vector3.UnitX, 10, out _));
        Assert.False(view.TryGetConstruct(new EntityId(1), out _));
        Assert.False(view.TryGetConstructPose(new EntityId(1), out _, out _));
        Assert.False(view.OverlapsConstruct(planet.Id, Vector3.Zero, Quaternion.Identity, null));
        Assert.Equal(0, view.CopyOtherPlayerFeet(planet.Id, new Vector3[2]));
    }

    private sealed class Sink : IFullSnapshotSink
    {
        public List<Snapshot> Snapshots { get; } = [];
        public void OnFullSnapshot(Snapshot full) => Snapshots.Add(full);
    }
}
