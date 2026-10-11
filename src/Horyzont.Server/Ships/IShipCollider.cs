using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Server.Ships;

/// <summary>
/// Continuous collision of a ship hull with the terrain and other constructs (K5-24 decision 9). The game implements it with the
/// engine (<c>GodotShipCollider</c>: a proxy of hull boxes and a body motion test); tests use sphere grounds. Main thread only.
/// </summary>
public interface IShipCollider
{
    /// <summary>Sweeps a ship from one pose to another in its parent frame; the hull never collides with its own blocks.</summary>
    /// <param name="ship">The ship construct.</param>
    /// <param name="startPose">Start pose of the construct origin in the parent frame.</param>
    /// <param name="endPose">End pose in the parent frame.</param>
    /// <returns>The hit, its safe fraction and its normal in the parent frame; <see cref="ShipSweepResult.Free"/> when nothing is hit or the ship is unknown.</returns>
    ShipSweepResult Sweep(EntityId ship, in PoseD startPose, in PoseD endPose);

    /// <summary>Releases what the collider keeps for a ship that left the ship mode or no longer exists.</summary>
    /// <param name="ship">The ship construct.</param>
    void Forget(EntityId ship);
}
