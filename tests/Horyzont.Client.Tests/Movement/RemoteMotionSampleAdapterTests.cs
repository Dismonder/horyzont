using System.Numerics;

using Horyzont.Client.Movement;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Movement;

public sealed class RemoteMotionSampleAdapterTests
{
    [Fact]
    public void Extract_PartialOrInvalidEntities_NeverInventsMissingFields()
    {
        var own = new EntityId(1);
        var complete = new EntityState(new EntityId(2), (EntityStateFields)15, 0, 7, Vector3.UnitX, Quaternion.Identity, Vector3.Zero, 1);
        var snapshot = new Snapshot(1, new Tick(100), 0, 0,
            [new SnapshotFrame(FrameRef.Of(new PlanetId(1)), 7)],
            [complete with { Entity = own }, complete, complete with { Entity = new EntityId(3), Fields = (EntityStateFields)1 },
                complete with { Entity = new EntityId(4), FrameIndex = 2 }, complete with { Entity = new EntityId(5), Position = new Vector3(float.NaN) }]);
        Span<RemoteMotionSample> samples = stackalloc RemoteMotionSample[8];
        Assert.Equal(1, RemoteMotionSampleAdapter.Extract(snapshot, own, samples));
        Assert.Equal(complete.Entity, samples[0].Entity);
        Assert.Equal(7u, samples[0].ParentSeq);
        Assert.Equal(0, RemoteMotionSampleAdapter.Extract(snapshot with { BaselineSnapshotId = 1 }, own, samples));
    }

    [Fact]
    public void Disconnect_Reconnect_ClearsPredictionAndInterpolation()
    {
        var time = new FakeTimeProvider();
        var adapter = new RemoteMotionSampleAdapter(time);
        var hello = new ServerHello(default, new Tick(100), 60, new PlayerId(1), new EntityId(1), [], [], []);
        adapter.OnServerHello(hello);
        var buffer = new RemoteMotionBuffer();
        adapter.Register(new EntityId(2), buffer);
        var state = new EntityState(new EntityId(2), (EntityStateFields)15, 0, 0, Vector3.Zero, Quaternion.Identity, Vector3.Zero, 1);
        adapter.OnFullSnapshot(new Snapshot(1, new Tick(103), 0, 0, [new SnapshotFrame(FrameRef.Of(new PlanetId(1)), 0)], [state]));
        Assert.Equal(1, buffer.Count);
        adapter.OnDisconnected(DisconnectCode.Normal);
        Assert.Equal(0, buffer.Count);
        Assert.Equal(0, adapter.Clock.RenderTick);
        adapter.OnServerHello(hello with { ServerTick = new Tick(1) });
        adapter.Register(new EntityId(2), buffer);
        adapter.OnFullSnapshot(new Snapshot(1, new Tick(2), 0, 0, [new SnapshotFrame(FrameRef.Of(new PlanetId(1)), 0)], [state]));
        Assert.Equal(1, buffer.Count);
        adapter.Remove(new EntityId(2));
        Assert.Equal(0, buffer.Count);
    }
}
