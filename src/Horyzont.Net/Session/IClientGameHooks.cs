using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Net.Session;

/// <summary>Game callbacks of a <see cref="ClientSession"/>, called from <see cref="ClientSession.Poll"/> or a session method.</summary>
public interface IClientGameHooks
{
    /// <summary>The server accepted the client; the game loads the world and sends TerrainCacheReport.</summary>
    /// <param name="hello">
    /// The message. Its content is checked by the codec only; throwing <see cref="InvalidDataException"/> or
    /// <see cref="Core.Serialization.ProtocolViolationException"/> here ends the session with ProtocolViolation.
    /// </param>
    void OnServerHello(ServerHello hello);

    /// <summary>The server refused the client.</summary>
    /// <param name="code">Reason.</param>
    /// <param name="detail">Human readable detail from the server.</param>
    void OnRejected(HandshakeRejectCode code, string detail);

    /// <summary>A server message arrived in the Loading or InGame state.</summary>
    /// <param name="message">
    /// The message. Throwing <see cref="InvalidDataException"/> or
    /// <see cref="Core.Serialization.ProtocolViolationException"/> ends the session with ProtocolViolation.
    /// </param>
    void OnMessage(Message message);

    /// <summary>The session ended; called once per <see cref="ClientSession.Connect"/>.</summary>
    /// <param name="code">Reason.</param>
    void OnDisconnected(DisconnectCode code);
}
