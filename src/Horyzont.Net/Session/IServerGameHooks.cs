using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Net.Session;

/// <summary>Game callbacks of a <see cref="ServerSession"/>, called from <see cref="ServerSession.Poll"/> or a session method.</summary>
public interface IServerGameHooks
{
    /// <summary>A client passed the handshake; the game applies PlayerJoinOp and returns the player.</summary>
    /// <param name="identity">Platform identity from ClientHello.</param>
    /// <param name="displayName">Display name from ClientHello.</param>
    /// <returns>The player, unique among connected players.</returns>
    PlayerId OnPlayerJoining(PlatformIdentity identity, string displayName);

    /// <summary>Builds the ServerHello for a joined player (YourPlayer and YourEntity from the world state).</summary>
    /// <param name="player">The player.</param>
    /// <returns>The message sent to the client.</returns>
    ServerHello CreateHello(PlayerId player);

    /// <summary>The client sent ClientReady; the game sends the initial state.</summary>
    /// <param name="player">The player.</param>
    void OnPlayerReady(PlayerId player);

    /// <summary>The client submitted an intent.</summary>
    /// <param name="player">The player.</param>
    /// <param name="clientSeq">Client sequence number of the intent.</param>
    /// <param name="intent">The intent.</param>
    /// <returns>The reply; the session always sends it with <paramref name="clientSeq"/>.</returns>
    IntentReply OnIntent(PlayerId player, uint clientSeq, Intent intent);

    /// <summary>The client sent TerrainCacheReport, TerrainChecksums or SnapshotAck.</summary>
    /// <param name="player">The player.</param>
    /// <param name="message">The message.</param>
    void OnClientMessage(PlayerId player, Message message);

    /// <summary>A joined player left; the game applies PlayerLeaveOp if the player is online.</summary>
    /// <param name="player">The player.</param>
    /// <param name="reason">Why the session ended.</param>
    void OnPlayerLeft(PlayerId player, DisconnectCode reason);
}
