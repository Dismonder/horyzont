using Horyzont.Core.Frames;

namespace Horyzont.Client.Navigation;

/// <summary>Where the player is in the world (system frame) at a render tick; implemented by the game from the own predicted state.</summary>
public interface INavigationViewpoint
{
    /// <summary>Gets the own position in the system frame.</summary>
    /// <param name="renderTick">Server tick being rendered (fractional).</param>
    /// <param name="position">Receives the position, metres.</param>
    /// <returns>False while the own state or one of its frames is unknown.</returns>
    bool TryGetWorldPosition(double renderTick, out Vector3D position);
}
