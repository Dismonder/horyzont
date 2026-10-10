using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Net.Session;

namespace Horyzont.Integration.Tests.Support;

/// <summary>Server game hooks over a <see cref="ServerSimulation"/>: joins, intents and leaves become journaled operations.</summary>
internal sealed class SimulationServerHooks(ServerSimulation sim) : IServerGameHooks
{
    public const byte TickRate = 30;

    private readonly Dictionary<PlatformIdentity, PlayerId> _joined = [];

    public HashSet<PlayerId> Ready { get; } = [];

    public List<(PlayerId Player, DisconnectCode Reason)> Left { get; } = [];

    public List<(PlayerId Player, IntentResult Result)> Results { get; } = [];

    public PlayerId PlayerOf(PlatformIdentity identity) => _joined[identity];

    public PlayerId OnPlayerJoining(PlatformIdentity identity, string displayName)
    {
        var op = sim.PreparePlayerJoin(identity, displayName);
        sim.ApplyServerOperation(op);
        _joined[identity] = op.Player;
        return op.Player;
    }

    public ServerHello CreateHello(PlayerId player)
    {
        sim.State.Players.TryGet(player, out var state);
        return new ServerHello(
            sim.State.Id, sim.State.CurrentTick, TickRate, player, state!.Entity, sim.State.Planets.All, sim.State.BlockTypes.Entries,
            sim.State.Materials.Entries);
    }

    public void OnPlayerReady(PlayerId player) => Ready.Add(player);

    public IntentReply OnIntent(PlayerId player, uint clientSeq, Intent intent)
    {
        var result = sim.Submit(player, intent);
        Results.Add((player, result));
        return new IntentReply(clientSeq, result.Reason, result.Applied?.Id ?? OpId.None);
    }

    public void OnClientMessage(PlayerId player, Message message)
    {
        // No client message influences the world in this test.
    }

    public void OnPlayerLeft(PlayerId player, DisconnectCode reason)
    {
        Left.Add((player, reason));
        if (sim.State.Players.TryGet(player, out var p) && p.IsOnline)
        {
            sim.ApplyServerOperation(new PlayerLeaveOp(player, p.Frame, p.ParentSeq, p.Position, p.BodyRotation, p.Pitch));
        }
    }
}
