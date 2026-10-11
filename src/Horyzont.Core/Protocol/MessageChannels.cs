namespace Horyzont.Core.Protocol;

/// <summary>Channel and frame size of each message type.</summary>
public static class MessageChannels
{
    /// <summary>Channel of a message type.</summary>
    /// <param name="type">Message type.</param>
    /// <returns>The channel.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined message type.</exception>
    public static DeliveryChannel For(MessageType type) => type switch
    {
        MessageType.InputBatch or MessageType.Snapshot or MessageType.SnapshotAck or MessageType.Ping or MessageType.Pong
            or MessageType.ShipInputBatch or MessageType.ShipCorrection
            => DeliveryChannel.Unreliable,
        MessageType.ClientHello or MessageType.ServerHello or MessageType.HandshakeReject or MessageType.ClientReady
            or MessageType.TerrainCacheReport or MessageType.TerrainBlockData or MessageType.TerrainChecksums
            or MessageType.ConstructState or MessageType.ConstructRemoved or MessageType.OperationApplied
            or MessageType.IntentSubmit or MessageType.IntentReply or MessageType.EntitySpawn or MessageType.EntityDespawn
            or MessageType.Disconnect or MessageType.TerrainSync or MessageType.TerrainView or MessageType.ResyncRequest or MessageType.ServerStatus or MessageType.JoinUnavailable
            or MessageType.StarSystemState
            => DeliveryChannel.ReliableOrdered,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown message type."),
    };

    /// <summary>Largest frame of a channel.</summary>
    /// <param name="channel">The channel.</param>
    /// <returns>256 KiB or 1150 B.</returns>
    public static int MaxFrameBytes(DeliveryChannel channel) => channel switch
    {
        DeliveryChannel.ReliableOrdered => ProtocolConstants.MaxReliableMessageBytes,
        DeliveryChannel.Unreliable => ProtocolConstants.MaxUnreliableMessageBytes,
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown channel."),
    };
}
