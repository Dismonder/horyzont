using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Game.Server.Characters;

/// <summary>Where <see cref="ServerCharacters"/> takes the input of a player from: one input per call (tests replace the session).</summary>
public interface IInputSource
{
    /// <summary>Takes the oldest buffered input of a player.</summary>
    /// <param name="player">The player.</param>
    /// <param name="input">The input.</param>
    /// <param name="seq">The client sequence number of the input.</param>
    /// <returns>False when no input is buffered.</returns>
    bool TryTakeInput(PlayerId player, out PlayerInput input, out uint seq);
}
