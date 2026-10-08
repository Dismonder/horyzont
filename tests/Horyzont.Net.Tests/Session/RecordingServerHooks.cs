using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Session;

namespace Horyzont.Net.Tests.Session;

public sealed class RecordingServerHooks : IServerGameHooks
{
    private ulong _nextPlayer = 1;

    public List<string> Calls { get; } = [];

    public List<(PlatformIdentity Identity, PlayerId Player)> Joined { get; } = [];

    public HashSet<PlayerId> Ready { get; } = [];

    public List<(PlayerId Player, DisconnectCode Reason)> Left { get; } = [];

    public List<(PlayerId Player, uint ClientSeq)> Intents { get; } = [];

    public List<(PlayerId Player, Message Message)> ClientMessages { get; } = [];

    /// <summary>When true, <see cref="OnPlayerLeft"/> records the call and then throws.</summary>
    public bool ThrowOnLeft { get; set; }

    public PlayerId OnPlayerJoining(PlatformIdentity identity, string displayName)
    {
        var player = new PlayerId(_nextPlayer++);
        Calls.Add("OnPlayerJoining");
        Joined.Add((identity, player));
        return player;
    }

    public ServerHello CreateHello(PlayerId player)
    {
        Calls.Add("CreateHello");
        return SessionHarness.HelloFor(player);
    }

    public void OnPlayerReady(PlayerId player)
    {
        Calls.Add("OnPlayerReady");
        Ready.Add(player);
    }

    public IntentReply OnIntent(PlayerId player, uint clientSeq, Intent intent)
    {
        Calls.Add("OnIntent");
        Intents.Add((player, clientSeq));

        // Deliberately not echoing clientSeq: the session must put the right one on the wire.
        return new IntentReply(0, RejectReason.None, new OpId(clientSeq + 1000));
    }

    public void OnClientMessage(PlayerId player, Message message)
    {
        Calls.Add("OnClientMessage");
        ClientMessages.Add((player, message));
    }

    public void OnPlayerLeft(PlayerId player, DisconnectCode reason)
    {
        Calls.Add("OnPlayerLeft");
        Left.Add((player, reason));
        if (ThrowOnLeft)
        {
            throw new InvalidOperationException("OnPlayerLeft failed (test).");
        }
    }

    public PlayerId PlayerOf(PlatformIdentity identity) => Joined.Single(j => j.Identity == identity).Player;
}
