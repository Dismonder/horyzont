using Horyzont.Client.Frames;
using Horyzont.Client.Replication;
using Horyzont.Client.Space;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Space;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Space;

public sealed class ClientStarSystemTests
{
    [Fact]
    public void ClientStarSystem_BeforeState_HomeOnly_AfterState_AllBodies()
    {
        var created = StarSystemGenesis.CreateWorldBodies(11, "core:test/1");
        var hello = new ServerHello(default, new Tick(0), 60, new PlayerId(1), new EntityId(1), created.Bodies, [], []);
        var stars = new ClientStarSystem();
        var time = new FakeTimeProvider();
        var frames = new ClientFrames(new EntityMirror(time), new ConstructMirror(time), stars);
        stars.OnServerHello(hello);
        frames.OnServerHello(hello);
        var graph = frames.Graph!;
        var home = created.Bodies[0].Id;
        var moon = created.Bodies[1].Id;

        Assert.Equal(home, stars.HomePlanet);
        Assert.Equal(created.Bodies[0].DayLengthSeconds, stars.SunCycleSeconds);
        Assert.True(graph.TryGetWorldPose(FrameRef.Of(home), 0, out var homePose));
        Assert.Equal(PoseD.Identity, homePose);
        Assert.False(graph.TryGetWorldPose(FrameRef.Of(moon), 0, out _));

        stars.OnMessage(new StarSystemState(created.System));

        Assert.NotNull(stars.Model);
        Assert.Equal(created.System, stars.System);
        Assert.Equal(created.System.SunCycleSeconds, stars.SunCycleSeconds);
        for (var k = 0; k < created.Bodies.Count; k++)
        {
            Assert.True(graph.TryGetWorldPose(FrameRef.Of(created.Bodies[k].Id), 0, out var pose));
            Assert.Equal(created.System.Bodies[k].Pose, pose);
        }

        stars.OnDisconnected(Core.Protocol.DisconnectCode.Normal);
        Assert.Null(stars.System);
        Assert.False(graph.TryGetWorldPose(FrameRef.Of(moon), 0, out _));
    }

    [Fact]
    public void ClientStarSystem_SystemNotMatchingHello_ProtocolViolation()
    {
        var created = StarSystemGenesis.CreateWorldBodies(11, "core:test/1");
        var stars = new ClientStarSystem();
        stars.OnServerHello(new ServerHello(default, new Tick(0), 60, new PlayerId(1), new EntityId(1), [created.Bodies[1], created.Bodies[0]], [], []));
        Assert.Throws<ProtocolViolationException>(() => stars.Apply(created.System));

        stars.OnServerHello(new ServerHello(default, new Tick(0), 60, new PlayerId(1), new EntityId(1), [created.Bodies[0]], [], []));
        Assert.Throws<ProtocolViolationException>(() => stars.Apply(created.System));
        Assert.Null(stars.System);
    }
}
