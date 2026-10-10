using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Simulation;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests;

public sealed class GameServerHooksTests
{
    private static readonly PlatformIdentity _alice = new(IdentityPlatform.Dev, 1);
    private static readonly PlatformIdentity _bob = new(IdentityPlatform.Dev, 2);

    private readonly InMemoryJournal _journal = new();
    private readonly FakeTerrainService _terrain = new();
    private readonly ServerSimulation _sim;
    private bool _accepts = true;

    public GameServerHooksTests()
    {
        _sim = new ServerSimulation(WorldFactory.CreateDefault(), _terrain, _journal, new GameRules(), NullLogger<ServerSimulation>.Instance);
        Hooks = new GameServerHooks(_sim, () => _accepts, NullLogger<GameServerHooks>.Instance);
    }

    private GameServerHooks Hooks { get; }

    [Fact]
    public void OnClientMessage_Handled_DoesNotShortCircuitRegistrationOrder()
    {
        var calls = new List<int>();
        var hooks = new GameServerHooks(_sim, () => true, NullLogger<GameServerHooks>.Instance,
            handlers: [new Handler(1, true, calls), new Handler(2, false, calls), new Handler(3, true, calls)]);
        hooks.OnClientMessage(new PlayerId(7), new SnapshotAck(1));
        Assert.Equal([1, 2, 3], calls);
    }

    private sealed class Handler(int id, bool handled, List<int> calls) : IServerClientMessageHandler
    {
        public bool Handle(PlayerId player, Horyzont.Core.Protocol.Messages.Message message)
        {
            calls.Add(id);
            return handled;
        }
    }

    [Fact]
    public void OnIntent_SubmitsAndRepliesWithClientSeq()
    {
        var player = Hooks.OnPlayerJoining(_alice, "alice");
        var applied = Hooks.OnIntent(player, 41, Dig(player, 5f));
        var rejected = Hooks.OnIntent(player, 42, Dig(player, 5f) with { Radius = 0.1f });

        Assert.Equal(41u, applied.ClientSeq);
        Assert.Equal(RejectReason.None, applied.Reason);
        Assert.Equal(_journal.Entries[^1].Id, applied.AppliedOpId);
        Assert.Equal(42u, rejected.ClientSeq);
        Assert.Equal(RejectReason.InvalidParameters, rejected.Reason);
        Assert.Equal(OpId.None, rejected.AppliedOpId);
    }

    [Fact]
    public void OnIntent_ServerReadOnly_RepliesServerReadOnlyWithoutSubmitting()
    {
        var player = Hooks.OnPlayerJoining(_alice, "alice");
        var before = StateHasher.Hash(_sim.State);
        var entries = _journal.Entries.Count;
        _accepts = false;

        var reply = Hooks.OnIntent(player, 7, Dig(player, 5f));

        Assert.Equal(new IntentReply(7, RejectReason.ServerReadOnly, OpId.None), reply);
        Assert.Equal(before, StateHasher.Hash(_sim.State));
        Assert.Equal(entries, _journal.Entries.Count);
        Assert.Empty(_terrain.AppliedEdits);
    }

    [Fact]
    public void OnPlayerJoining_ServerReadOnly_ThrowsWithoutApplying()
    {
        _accepts = false;
        var before = StateHasher.Hash(_sim.State);

        Assert.Throws<InvalidOperationException>(() => Hooks.OnPlayerJoining(_alice, "alice"));

        Assert.Equal(before, StateHasher.Hash(_sim.State));
        Assert.Empty(_journal.Entries);
    }

    [Fact]
    public void OnPlayerJoining_IdentityAlreadyOnline_ThrowsWithoutApplying()
    {
        var first = Hooks.OnPlayerJoining(_alice, "alice");
        var before = StateHasher.Hash(_sim.State);
        var entries = _journal.Entries.Count;

        Assert.Throws<InvalidOperationException>(() => Hooks.OnPlayerJoining(_alice, "alice again"));

        Assert.Equal(before, StateHasher.Hash(_sim.State));
        Assert.Equal(entries, _journal.Entries.Count);
        Assert.True(_sim.State.Players.TryGet(first, out var player) && player.IsOnline);
    }

    [Fact]
    public void TwoNewIdentitiesInOnePoll_GetDistinctPlayers()
    {
        var a = Hooks.OnPlayerJoining(_alice, "alice");
        var b = Hooks.OnPlayerJoining(_bob, "bob");

        Assert.NotEqual(a, b);
        Assert.Equal(2, _sim.State.Players.Online.Count());
        Assert.NotEqual(Hooks.CreateHello(a).YourEntity, Hooks.CreateHello(b).YourEntity);
    }

    [Fact]
    public void OnPlayerJoining_KnownOfflineIdentity_ReturnsTheSamePlayer()
    {
        var first = Hooks.OnPlayerJoining(_alice, "alice");
        Hooks.OnPlayerLeft(first, Core.Protocol.DisconnectCode.Normal);

        var again = Hooks.OnPlayerJoining(_alice, "alice");

        Assert.Equal(first, again);
    }

    [Fact]
    public void CreateHello_UsesWorldRegistriesAndTickRate()
    {
        var player = Hooks.OnPlayerJoining(_alice, "alice");

        var hello = Hooks.CreateHello(player);

        Assert.Equal(_sim.State.Id, hello.World);
        Assert.Equal(player, hello.YourPlayer);
        Assert.Equal(GameServerHooks.TickRate, hello.TickRate);
        Assert.Equal(_sim.State.Planets.All, hello.Planets);
        Assert.Equal(_sim.State.BlockTypes.Entries, hello.BlockTypes);
        Assert.Equal(_sim.State.Materials.Entries, hello.Materials);
        Assert.Throws<InvalidOperationException>(() => Hooks.CreateHello(new PlayerId(99)));
    }

    /// <summary>The leave op goes through the strict codec check; the stored rotation is only within the pose tolerance of the applier.</summary>
    [Theory]
    [InlineData(0.0009f)]
    [InlineData(-0.0009f)]
    public void OnPlayerLeft_DenormalizedRotation_SerializesLeaveOp(float lengthError)
    {
        var player = Hooks.OnPlayerJoining(_alice, "alice");
        SetRotationLengthError(player, lengthError);

        Hooks.OnPlayerLeft(player, Core.Protocol.DisconnectCode.Normal);

        var leave = Assert.IsType<PlayerLeaveOp>(_journal.Entries[^1].Operation);
        var writer = new ByteWriter();
        OperationCodec.Write(writer, leave);
        Assert.Equal(1f, leave.BodyRotation.Length(), 1e-6f);
        Assert.False(_sim.State.Players.TryGet(player, out var state) && state.IsOnline);
    }

    [Theory]
    [InlineData(0.00099f)]
    [InlineData(-0.00099f)]
    [InlineData(0.000999f)]
    public void LeaveAllOnlinePlayers_RotationAtPoseTolerance_Serializes(float lengthError)
    {
        var player = Hooks.OnPlayerJoining(_alice, "alice");
        SetRotationLengthError(player, lengthError);

        _sim.LeaveAllOnlinePlayers();

        var leave = Assert.IsType<PlayerLeaveOp>(_journal.Entries[^1].Operation);
        var writer = new ByteWriter();
        OperationCodec.Write(writer, leave);
        Assert.False(_sim.State.Players.TryGet(player, out var state) && state.IsOnline);
    }

    [Fact]
    public void OnPlayerLeft_SimulationFaulted_SkipsTheLeave()
    {
        var player = Hooks.OnPlayerJoining(_alice, "alice");
        _terrain.ThrowOnApply = new InvalidOperationException("boom");
        Assert.Throws<InvalidOperationException>(() => Hooks.OnIntent(player, 1, Dig(player, 5f)));
        Assert.True(_sim.IsFaulted);
        var entries = _journal.Entries.Count;

        Hooks.OnPlayerLeft(player, Core.Protocol.DisconnectCode.Timeout);

        Assert.Equal(entries, _journal.Entries.Count);
    }

    [Fact]
    public void OnPlayerLeft_ReadOnly_StillAppliesTheLeave()
    {
        var player = Hooks.OnPlayerJoining(_alice, "alice");
        _accepts = false;

        Hooks.OnPlayerLeft(player, Core.Protocol.DisconnectCode.Normal);

        Assert.False(_sim.State.Players.TryGet(player, out var state) && state.IsOnline);
    }

    private void SetRotationLengthError(PlayerId player, float lengthError)
    {
        var scale = 1f + lengthError;
        var rotation = new Quaternion(0f, 0f, 0f, scale);
        Assert.True(_sim.State.Players.TryGet(player, out var state));
        Assert.True(_sim.UpdatePlayerKinematics(player, state.Frame, state.ParentSeq, state.Position, rotation, 0f));
    }

    private TerrainToolIntent Dig(PlayerId player, float x)
    {
        _sim.State.Players.TryGet(player, out var p);
        var eye = p!.EyePosition(_sim.Rules);
        var r = _terrain.SurfaceRadius;
        var ground = new Vector3(x, MathF.Sqrt((r * r) - (x * x)), 0f);
        return new TerrainToolIntent(WorldFactory.HomePlanet, eye, Vector3.Normalize(ground - eye), 1f, TerrainEditMode.Remove, 0);
    }
}
