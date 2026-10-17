using Horyzont.Core.World;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>
/// The settings of the world (message 47, K6-35 D4): sent to every player after the containers and before message 48, and again after every change; reliable ordered, server to
/// client. Format 1: a flag byte (bit 0 teleport everything, bit 1 free fuel, bit 2 free building; every other bit is an error), three <c>u16</c> (resources 50 to 300, stacks 100 to 300,
/// hazards 50 to 200, in percent), the building integrity (1 to 3) and the death penalty (1 to 3).
/// </summary>
/// <param name="Settings">The settings.</param>
public sealed record WorldSettingsState(WorldSettings Settings) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.WorldSettingsState;
}
