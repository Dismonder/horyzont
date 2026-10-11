using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Transport;

namespace Horyzont.Net.Session;

/// <summary>Server-side state of one client connection.</summary>
internal sealed class Peer
{
    public Peer(ConnectionId connection, ServerOptions options, long nowTicks)
    {
        Connection = connection;
        ConnectedAt = nowTicks;
        LastReceived = nowTicks;
        DropWindowStart = nowTicks;
        Reliable = new MessageBucket(options.ReliableMessagesPerSecond, nowTicks);
        Unreliable = new MessageBucket(options.UnreliableMessagesPerSecond, nowTicks);
    }

    public ConnectionId Connection { get; }

    public PeerState State { get; set; } = PeerState.AwaitingHello;

    /// <summary>Gets or sets the player after OnPlayerJoining; <see cref="PlayerId.None"/> before.</summary>
    public PlayerId Player { get; set; }

    public PlatformIdentity Identity { get; set; }

    public long ConnectedAt { get; }

    public long LastReceived { get; set; }

    public long CloseAt { get; set; }

    /// <summary>Gets or sets the code this side closed with.</summary>
    public DisconnectCode LocalCode { get; set; }

    public MessageBucket Reliable { get; }

    public MessageBucket Unreliable { get; }

    public long DropWindowStart { get; set; }

    public int DropsInWindow { get; set; }

    /// <summary>Gets or sets the input buffer, allocated when the player joins; null before the handshake.</summary>
    public InputBuffer? Inputs { get; set; }

    /// <summary>Gets or sets the pilot input buffer (K5-25), allocated with <see cref="Inputs"/>.</summary>
    public SequencedInputBuffer<ShipInput>? ShipInputs { get; set; }

    /// <summary>Gets a value indicating whether the player passed OnPlayerJoining and has not left.</summary>
    public bool IsJoined => State is PeerState.Loading or PeerState.InGame;
}
