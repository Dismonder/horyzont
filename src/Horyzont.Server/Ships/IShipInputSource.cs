using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Server.Ships;

/// <summary>Buffered pilot inputs per player (K5-25 decision 12); <see cref="SessionShipInputs"/> reads the server session.</summary>
public interface IShipInputSource
{
    /// <summary>Consumes input slot <paramref name="slot"/>: drops older inputs, returns the input of the slot when it arrived.</summary>
    /// <param name="player">The player.</param>
    /// <param name="slot">The slot.</param>
    /// <param name="input">The input of the slot.</param>
    /// <returns>True when the input of the slot was buffered.</returns>
    bool TryTakeShipInput(PlayerId player, uint slot, out ShipInput input);

    /// <summary>The newest input number received since the last clear, buffered or refused as late.</summary>
    /// <param name="player">The player.</param>
    /// <param name="seq">The number.</param>
    /// <returns>False when nothing is buffered.</returns>
    bool TryPeekNewestShipSeq(PlayerId player, out uint seq);

    /// <summary>New inputs refused because their slot was already consumed (not repeated copies), since the last clear.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The count.</returns>
    int ShipInputLateCount(PlayerId player);

    /// <summary>Accepts inputs numbered from <paramref name="slot"/> again; buffered inputs are kept (re-anchoring behind, R1).</summary>
    /// <param name="player">The player.</param>
    /// <param name="slot">The first slot to accept again.</param>
    void RewindShipInputs(PlayerId player, uint slot);

    /// <summary>Forgets the buffered inputs and the consumed slot.</summary>
    /// <param name="player">The player.</param>
    void ClearShipInputs(PlayerId player);
}
