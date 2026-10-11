using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Server.Frames;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing;

namespace Horyzont.Server.Tests.Frames;

public sealed class ServerFramesBinderTests
{
    [Fact]
    public async Task ServerFramesBinder_CreatesFramesWithTheWorld_RecordsShipsInCompose()
    {
        await using var rig = await ReplicationRig.StartAsync();
        var binder = new ServerFramesBinder();
        Assert.Null(binder.Transitions);
        Assert.Equal(ServerTickPhase.Compose, binder.Phase);
        binder.Register(rig.Runtime);
        var ship = rig.AddShip(new Vector3(0f, 4010f, 0f));
        rig.Step(3);

        Assert.NotNull(binder.Transitions);
        Assert.True(binder.History!.HasTick);
        Assert.True(binder.History.History.TryGetWorldPose(FrameRef.Of(ship), binder.History.LastTick.Value, out var pose));
        Assert.Equal(4010, pose.Position.Y, 3);
        Assert.True(binder.Transitions!.Graph.TryGetWorldPose(FrameRef.Of(WorldFactory.HomePlanet), out _));
    }
}
