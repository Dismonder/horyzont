using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Session;

namespace Horyzont.Net.Tests.Session;

public sealed class RecordingClientHooks : IClientGameHooks
{
    public ClientSession? Session { get; set; }

    public ServerHello? Hello { get; private set; }

    public List<(HandshakeRejectCode Code, string Detail)> Rejections { get; } = [];

    public List<Message> Messages { get; } = [];

    public List<(DisconnectCode Code, ClientState StateAtCall)> Disconnections { get; } = [];

    public Func<ServerHello, Exception?>? ThrowOnHello { get; set; }

    public void OnServerHello(ServerHello hello)
    {
        Hello = hello;
        if (ThrowOnHello?.Invoke(hello) is { } exception)
        {
            throw exception;
        }
    }

    public void OnRejected(HandshakeRejectCode code, string detail) => Rejections.Add((code, detail));

    public void OnMessage(Message message) => Messages.Add(message);

    public void OnDisconnected(DisconnectCode code) => Disconnections.Add((code, Session?.State ?? ClientState.Disconnected));
}
