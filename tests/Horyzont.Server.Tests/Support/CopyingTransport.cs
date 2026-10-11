using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Net.Transport;

namespace Horyzont.Server.Tests.Support;

/// <summary>A transport with eight clients that joined; it copies every sent payload and counts snapshots (allocation tests).</summary>
internal sealed class CopyingTransport : ITransport
{
    private readonly byte[] _copy = new byte[ProtocolConstants.MaxReliableMessageBytes];
    private bool _joined;

    private readonly uint[] _perConnection = new uint[16];

    public int SnapshotsSent { get; private set; }

    public bool Inspect { get; set; }

    public int DeltasSeen { get; private set; }

    public uint SentTo(ulong connection) => _perConnection[connection];

    public int MaxReliablePayload => ProtocolConstants.MaxReliableMessageBytes;

    public int MaxUnreliablePayload => ProtocolConstants.MaxUnreliableMessageBytes;

    public bool IsDrained => true;

    public void Send(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload)
    {
        payload.CopyTo(_copy);
        if (payload[0] == (byte)MessageType.Snapshot)
        {
            SnapshotsSent++;
            _perConnection[connection.Value]++;
            if (Inspect && MessageCodec.Read(payload) is Snapshot { BaselineSnapshotId: not 0 })
            {
                DeltasSeen++;
            }
        }
    }

    public void Poll(ITransportEvents events)
    {
        if (_joined)
        {
            return;
        }

        _joined = true;
        var writer = new ByteWriter();
        for (ulong id = 1; id <= 8; id++)
        {
            var connection = new ConnectionId(id);
            events.OnConnected(connection);
            writer.Reset();
            MessageCodec.Write(writer, new ClientHello(ProtocolConstants.ProtocolVersion, 1,
                new PlatformIdentity(IdentityPlatform.Dev, id), "player", "", []));
            events.OnReceived(connection, TransportChannel.ReliableOrdered, writer.WrittenSpan);
            writer.Reset();
            MessageCodec.Write(writer, new ClientReady());
            events.OnReceived(connection, TransportChannel.ReliableOrdered, writer.WrittenSpan);
        }
    }

    public void Listen(TransportEndpoint endpoint)
    {
    }

    public ConnectionId Connect(TransportEndpoint endpoint) => throw new NotSupportedException();

    public void Disconnect(ConnectionId connection)
    {
    }

    public TransportStats GetStats(ConnectionId connection) => default;

    public TransportEndpoint GetRemoteEndpoint(ConnectionId connection) => new InMemoryEndpoint("client");

    public void Dispose()
    {
    }
}
