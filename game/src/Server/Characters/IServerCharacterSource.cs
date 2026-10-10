using Horyzont.Core.Ids;

namespace Horyzont.Game.Server.Characters;

/// <summary>Main-thread read of authoritative character kinematics.</summary>
public interface IServerCharacterSource
{
    /// <summary>Reads the latest state of a spawned player.</summary>
    /// <param name="player">The player.</param>
    /// <param name="state">The character state, when available.</param>
    /// <returns>Whether the player has a body.</returns>
    bool TryGetState(PlayerId player, out ServerCharacterState state);
}
