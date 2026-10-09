using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;

namespace Horyzont.Server;

/// <summary>
/// A system that follows the sessions of players on the main thread: it is told when a player is ready (the client loaded and sent
/// ready) and when a player leaves, before the leave is applied to the simulation. Registered with
/// <see cref="ServerRuntime.AddPlayerObserver"/> before the world starts.
/// </summary>
public interface IServerPlayerObserver
{
    /// <summary>The player finished loading and entered the game.</summary>
    /// <param name="player">The player.</param>
    void OnPlayerReady(PlayerId player);

    /// <summary>
    /// The session of the player ended. Called before <c>PlayerLeaveOp</c> is applied, so the observer can write the last kinematics
    /// of the player and release what depends on it. An exception is logged and does not stop the leave.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="reason">Why the session ended.</param>
    void OnPlayerLeaving(PlayerId player, DisconnectCode reason);
}
