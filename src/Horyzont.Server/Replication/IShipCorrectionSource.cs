using Horyzont.Core.Ids;

namespace Horyzont.Server.Replication;

/// <summary>Encodes the ship correction of a pilot for the replication round (K5-25 decision 15).</summary>
public interface IShipCorrectionSource
{
    /// <summary>Writes the correction frame of the ship the player pilots.</summary>
    /// <param name="player">Recipient.</param>
    /// <param name="destination">Frame buffer.</param>
    /// <param name="ship">The ship of the correction.</param>
    /// <returns>The frame length; 0 when the player pilots no ship (or the state is not ready).</returns>
    int Write(PlayerId player, Span<byte> destination, out EntityId ship);
}
