using Horyzont.Core.Space;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>
/// The star system of the world (K5-21 decision 14): sent by the server once per player session after <see cref="ServerHello"/>,
/// in loading or in game, and only when the world has a star system. Same payload as blob 10.
/// </summary>
/// <param name="System">The star system.</param>
public sealed record StarSystemState(StarSystemParams System) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.StarSystemState;
}
