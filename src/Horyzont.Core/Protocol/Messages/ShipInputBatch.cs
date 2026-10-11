namespace Horyzont.Core.Protocol.Messages;

/// <summary>
/// C→S, unreliable: 1–5 consecutive pilot inputs, the newest and up to three earlier ones repeated (K5-25 decision 11).
/// </summary>
/// <param name="FirstSeq">Sequence number of the first input.</param>
/// <param name="Inputs">Inputs with consecutive numbers from <paramref name="FirstSeq"/>.</param>
public sealed record ShipInputBatch(uint FirstSeq, IReadOnlyList<ShipInput> Inputs) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.ShipInputBatch;
}
