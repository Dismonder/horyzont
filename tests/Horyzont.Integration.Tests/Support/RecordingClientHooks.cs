using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Session;

namespace Horyzont.Integration.Tests.Support;

/// <summary>Client hooks that only record what the server sent.</summary>
internal sealed class RecordingClientHooks : IClientGameHooks
{
    public ServerHello? Hello { get; private set; }

    public List<IntentReply> Replies { get; } = [];

    public List<Message> Messages { get; } = [];

    public List<DisconnectCode> Disconnections { get; } = [];

    public void OnServerHello(ServerHello hello) => Hello = hello;

    public void OnRejected(HandshakeRejectCode code, string detail) =>
        throw new InvalidOperationException($"Client rejected: {code} {detail}");

    public void OnMessage(Message message)
    {
        Messages.Add(message);
        if (message is IntentReply reply)
        {
            Replies.Add(reply);
        }
    }

    public void OnDisconnected(DisconnectCode code) => Disconnections.Add(code);
}
