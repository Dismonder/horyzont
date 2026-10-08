namespace Horyzont.Core.Protocol.Messages;

/// <summary>Client received a snapshot.</summary>
/// <param name="SnapshotId">Snapshot number.</param>
public sealed record SnapshotAck(uint SnapshotId) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.SnapshotAck;
}
