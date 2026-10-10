using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Session;
using Horyzont.Net.Transport.InMemory;

namespace Horyzont.Server.Tests.Support;

/// <summary>A client of the rig: its transport, session and what the server told it.</summary>
internal sealed class RigClient : IClientGameHooks
{
    public RigClient(InMemoryNetwork network, string name, PlatformIdentity identity, ulong build, TimeProvider time)
    {
        Name = name;
        Identity = identity;
        Transport = network.CreateTransport(name, Net.Transport.TransportOptions.Default);
        Session = new ClientSession(Transport, new ClientOptions { BuildHash = build }, this, time);
    }

    public string Name { get; }

    public PlatformIdentity Identity { get; }

    public InMemoryTransport Transport { get; }

    public ClientSession Session { get; }

    public ServerHello? Hello { get; private set; }

    public List<IntentReply> Replies { get; } = [];

    /// <summary>Every game message the client received, in arrival order.</summary>
    public List<Message> Received { get; } = [];

    /// <summary>Called for every game message before it is recorded; a replication test plugs its client mirrors in here.</summary>
    public Action<Message>? Observer { get; set; }

    public List<DisconnectCode> Disconnections { get; } = [];

    public List<HandshakeRejectCode> Rejections { get; } = [];

    public PlayerId Player => Hello!.YourPlayer;

    public void OnServerHello(ServerHello hello) => Hello = hello;

    public void OnRejected(HandshakeRejectCode code, string detail) => Rejections.Add(code);

    public void OnMessage(Message message)
    {
        Received.Add(message);
        Observer?.Invoke(message);
        if (message is IntentReply reply)
        {
            Replies.Add(reply);
        }
    }

    public void OnDisconnected(DisconnectCode code) => Disconnections.Add(code);
}
