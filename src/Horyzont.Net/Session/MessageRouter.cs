using Horyzont.Core.Protocol;

namespace Horyzont.Net.Session;

/// <summary>
/// Which message types each side may receive in each state. Checked on the raw type byte before decoding, so a peer
/// cannot make the receiver decode (and allocate for) a type it never accepts.
/// </summary>
internal static class MessageRouter
{
    /// <summary>Reads the type byte of a frame.</summary>
    /// <param name="frame">Frame bytes.</param>
    /// <param name="type">The message type when the method returns true.</param>
    /// <returns>True when the frame starts with a defined message type.</returns>
    public static bool TryGetType(ReadOnlySpan<byte> frame, out MessageType type)
    {
        if (frame.Length >= 1 && frame[0] >= (byte)MessageType.ClientHello && frame[0] <= (byte)MessageType.Disconnect)
        {
            type = (MessageType)frame[0];
            return true;
        }

        type = default;
        return false;
    }

    /// <summary>Whether the server accepts <paramref name="type"/> from a client in <paramref name="state"/>.</summary>
    public static bool IsAllowedFromClient(PeerState state, MessageType type) => state switch
    {
        PeerState.AwaitingHello => type is MessageType.ClientHello or MessageType.Disconnect,
        PeerState.Loading => type is MessageType.TerrainCacheReport or MessageType.ClientReady or MessageType.Disconnect or MessageType.Ping,
        PeerState.InGame => type is MessageType.IntentSubmit or MessageType.TerrainCacheReport or MessageType.TerrainChecksums
            or MessageType.Disconnect or MessageType.InputBatch or MessageType.SnapshotAck or MessageType.Ping,
        _ => false,
    };

    /// <summary>Whether the client accepts <paramref name="type"/> from the server in <paramref name="state"/>.</summary>
    public static bool IsAllowedFromServer(ClientState state, MessageType type) => state switch
    {
        ClientState.AwaitingServerHello => type is MessageType.ServerHello or MessageType.HandshakeReject or MessageType.Disconnect,
        ClientState.Loading or ClientState.InGame => type == MessageType.Disconnect || IsGameMessageFromServer(type),
        _ => false,
    };

    /// <summary>Server-to-client types the game sends itself (not handshake or session control).</summary>
    public static bool IsGameMessageFromServer(MessageType type) => type is MessageType.TerrainBlockData or MessageType.ConstructState
        or MessageType.ConstructRemoved or MessageType.OperationApplied or MessageType.IntentReply or MessageType.Snapshot
        or MessageType.EntitySpawn or MessageType.EntityDespawn or MessageType.Pong;

    /// <summary>Client-to-server types the game sends itself through <see cref="ClientSession.Send"/>.</summary>
    public static bool IsGameMessageFromClient(MessageType type) =>
        type is MessageType.TerrainCacheReport or MessageType.TerrainChecksums or MessageType.SnapshotAck;
}
