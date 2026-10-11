using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Session;

namespace Horyzont.Server.Ships;

/// <summary>Pilot inputs of the server session (K5-25).</summary>
/// <param name="session">The session.</param>
public sealed class SessionShipInputs(ServerSession session) : IShipInputSource
{
    /// <inheritdoc />
    public bool TryTakeShipInput(PlayerId player, uint slot, out ShipInput input) => session.TryTakeShipInput(player, slot, out input);

    /// <inheritdoc />
    public bool TryPeekNewestShipSeq(PlayerId player, out uint seq) => session.TryPeekNewestShipSeq(player, out seq);

    /// <inheritdoc />
    public int ShipInputLateCount(PlayerId player) => session.ShipInputLateCount(player);

    /// <inheritdoc />
    public void RewindShipInputs(PlayerId player, uint slot) => session.RewindShipInputs(player, slot);

    /// <inheritdoc />
    public void ClearShipInputs(PlayerId player) => session.ClearShipInputs(player);
}
