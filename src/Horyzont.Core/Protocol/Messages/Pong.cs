using Horyzont.Core.Ids;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>Reply to <see cref="Ping"/>.</summary>
/// <param name="ClientTimeMicros">Echoed client clock.</param>
/// <param name="ServerTick">Server tick.</param>
public sealed record Pong(ulong ClientTimeMicros, Tick ServerTick) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.Pong;
}
