using Horyzont.Core.Checkpoints;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>Chunks of one construct; part 0 replaces the client copy, further parts complete it.</summary>
/// <param name="Header">Construct header.</param>
/// <param name="PartIndex">Part number.</param>
/// <param name="PartCount">Number of parts.</param>
/// <param name="Chunks">Chunks of this part.</param>
public sealed record ConstructState(
    ConstructHeaderRecord Header, ushort PartIndex, ushort PartCount, IReadOnlyList<ConstructChunkRecord> Chunks) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.ConstructState;
}
