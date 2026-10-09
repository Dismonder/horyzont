using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Simulation;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests;

public sealed class PlayerObserverTests
{
    private static readonly PlatformIdentity _alice = new(IdentityPlatform.Dev, 1);

    private readonly InMemoryJournal _journal = new();
    private readonly ServerSimulation _sim;

    public PlayerObserverTests() =>
        _sim = new ServerSimulation(WorldFactory.CreateDefault(), new FakeTerrainService(), _journal, new GameRules(), NullLogger<ServerSimulation>.Instance);

    [Fact]
    public void OnPlayerLeft_ObserverRunsBeforeTheLeaveOp_AndSeesTheOnlinePlayer()
    {
        var observer = new RecordingObserver(_sim, _journal);
        var hooks = new GameServerHooks(_sim, () => true, NullLogger<GameServerHooks>.Instance, [observer]);
        var player = hooks.OnPlayerJoining(_alice, "alice");

        hooks.OnPlayerReady(player);
        hooks.OnPlayerLeft(player, DisconnectCode.Normal);

        Assert.Equal([player], observer.Ready);
        Assert.Equal([player], observer.Leaving);
        Assert.True(observer.WasOnlineWhenLeaving);
        Assert.Equal(0, observer.LeaveOpsSeenWhenLeaving);
        Assert.IsType<PlayerLeaveOp>(_journal.Entries[^1].Operation);
    }

    [Fact]
    public void OnPlayerLeft_ObserverThrows_TheLeaveIsStillApplied()
    {
        var hooks = new GameServerHooks(_sim, () => true, NullLogger<GameServerHooks>.Instance, [new ThrowingObserver()]);
        var player = hooks.OnPlayerJoining(_alice, "alice");

        hooks.OnPlayerLeft(player, DisconnectCode.Timeout);

        Assert.False(_sim.State.Players.TryGet(player, out var state) && state.IsOnline);
    }

    [Fact]
    public void OnPlayerJoining_NewIdentity_UsesTheSpawnPolicyAndAKnownOneKeepsItsPose()
    {
        var feet = new Vector3(4010f, 0f, 0f);
        var rotation = SurfaceFrame.FromYaw(Vector3.UnitX, 0f);
        var hooks = new GameServerHooks(_sim, () => true, NullLogger<GameServerHooks>.Instance, null, _ => (feet, rotation));

        var player = hooks.OnPlayerJoining(_alice, "alice");

        Assert.True(_sim.State.Players.TryGet(player, out var joined));
        Assert.Equal(feet, joined.Position);
        Assert.Equal(rotation, joined.BodyRotation);

        var moved = new Vector3(0f, 4005f, 0f);
        Assert.True(_sim.UpdatePlayerKinematics(player, joined.Frame, joined.ParentSeq, moved, Quaternion.Identity, 0f));
        hooks.OnPlayerLeft(player, DisconnectCode.Normal);

        var again = hooks.OnPlayerJoining(_alice, "alice");

        Assert.Equal(player, again);
        Assert.True(_sim.State.Players.TryGet(again, out var back));
        Assert.Equal(moved, back.Position);
    }

    private sealed class RecordingObserver(ServerSimulation sim, InMemoryJournal journal) : IServerPlayerObserver
    {
        public List<PlayerId> Ready { get; } = [];

        public List<PlayerId> Leaving { get; } = [];

        public bool WasOnlineWhenLeaving { get; private set; }

        public int LeaveOpsSeenWhenLeaving { get; private set; }

        public void OnPlayerReady(PlayerId player) => Ready.Add(player);

        public void OnPlayerLeaving(PlayerId player, DisconnectCode reason)
        {
            Leaving.Add(player);
            WasOnlineWhenLeaving = sim.State.Players.TryGet(player, out var state) && state.IsOnline;
            LeaveOpsSeenWhenLeaving = journal.Entries.Count(e => e.Operation is PlayerLeaveOp);
        }
    }

    private sealed class ThrowingObserver : IServerPlayerObserver
    {
        public void OnPlayerReady(PlayerId player) => throw new InvalidOperationException("boom");

        public void OnPlayerLeaving(PlayerId player, DisconnectCode reason) => throw new InvalidOperationException("boom");
    }
}
