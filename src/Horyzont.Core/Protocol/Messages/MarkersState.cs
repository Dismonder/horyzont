using Horyzont.Core.World.Markers;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>
/// The map markers a player may see (K5-27 decision 6): every shared marker and the player's own private markers, sent by the server
/// once per player session after <see cref="StarSystemState"/>. A private marker of another player never appears (the owner field
/// tells the two kinds apart). At most <see cref="ProtocolConstants.MaxMarkersInState"/> records.
/// </summary>
/// <param name="Markers">The markers in id order.</param>
public sealed record MarkersState(IReadOnlyList<MarkerRecord> Markers) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.MarkersState;
}
