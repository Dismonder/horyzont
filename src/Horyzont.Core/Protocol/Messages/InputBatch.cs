namespace Horyzont.Core.Protocol.Messages;

/// <summary>1–5 consecutive inputs of the client.</summary>
/// <param name="FirstSeq">Sequence number of the first input.</param>
/// <param name="Inputs">Inputs with consecutive numbers from <paramref name="FirstSeq"/>.</param>
public sealed record InputBatch(uint FirstSeq, IReadOnlyList<PlayerInput> Inputs) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.InputBatch;
}
