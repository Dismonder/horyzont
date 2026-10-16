using Horyzont.Core.Ids;

namespace Horyzont.Server.Survival;

/// <summary>Where the vitals service reads the motion of a body from; the game adapts its server characters (K6-33 D13), the tests use a fake.</summary>
public interface IVitalsMotionSource
{
    /// <summary>Reads the motion of the body of a player.</summary>
    /// <param name="player">The player.</param>
    /// <param name="motion">The motion of this tick, when the player has a body.</param>
    /// <returns>Whether the player has a body.</returns>
    bool TryGetMotion(PlayerId player, out VitalsMotion motion);
}
