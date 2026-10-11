using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;

namespace Horyzont.Testing.Ships;

/// <summary>
/// Scripted ship motion for frame tests (K5-20): the construct origin runs on a horizontal circle at <paramref name="speed"/> m/s while
/// the construct turns about +Y at <paramref name="angularSpeed"/> rad/s; applied through <see cref="ServerSimulation.UpdateConstructKinematics"/>.
/// </summary>
/// <param name="centre">Centre of the circle in the parent frame.</param>
/// <param name="radius">Radius in metres.</param>
/// <param name="speed">Speed along the circle in m/s.</param>
/// <param name="angularSpeed">Turn rate of the construct in rad/s.</param>
/// <param name="tickSeconds">Length of a tick.</param>
public sealed class ShipCircleScript(Vector3 centre, float radius, float speed = 30f, float angularSpeed = 0.3f, double tickSeconds = 1.0 / 60.0)
{
    /// <summary>Pose of the construct at a tick.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>Origin and rotation in the parent frame.</returns>
    public (Vector3 Origin, Quaternion Rotation) PoseAt(Tick tick)
    {
        var seconds = tick.Value * tickSeconds;
        var angle = seconds * speed / radius;
        var origin = centre + new Vector3((float)(radius * Math.Cos(angle)), 0f, (float)(radius * Math.Sin(angle)));
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(angularSpeed * seconds % (2 * Math.PI)));
        return (origin, rotation);
    }

    /// <summary>Moves the construct to its pose at the current tick of the simulation, in its current frame.</summary>
    /// <param name="sim">The simulation.</param>
    /// <param name="ship">The construct.</param>
    /// <returns>The result of <see cref="ServerSimulation.UpdateConstructKinematics"/>; false for an unknown construct.</returns>
    public bool Step(ServerSimulation sim, EntityId ship)
    {
        ArgumentNullException.ThrowIfNull(sim);
        if (!sim.State.Constructs.TryGet(ship, out var construct))
        {
            return false;
        }

        var (origin, rotation) = PoseAt(sim.State.CurrentTick);
        return sim.UpdateConstructKinematics(ship, construct.Parent, construct.ParentSeq, origin, rotation);
    }
}
