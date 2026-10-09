using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Session;

/// <summary>Receives client session events on the main thread, in registration order.</summary>
public interface IClientSessionListener
{
    /// <summary>The server accepted the client; the game prepares the terrain and sends ready.</summary>
    /// <param name="hello">The message.</param>
    void OnServerHello(ServerHello hello)
    {
    }

    /// <summary>The server refused the client.</summary>
    /// <param name="code">Reason.</param>
    /// <param name="detail">Human readable detail from the server.</param>
    void OnRejected(HandshakeRejectCode code, string detail)
    {
    }

    /// <summary>The server answered an intent; it does not reach <see cref="OnMessage"/>.</summary>
    /// <param name="reply">The reply.</param>
    void OnIntentReply(IntentReply reply)
    {
    }

    /// <summary>Any other server-to-client game message.</summary>
    /// <param name="message">The message.</param>
    void OnMessage(Message message)
    {
    }

    /// <summary>The session ended.</summary>
    /// <param name="code">Reason.</param>
    void OnDisconnected(DisconnectCode code)
    {
    }
}
