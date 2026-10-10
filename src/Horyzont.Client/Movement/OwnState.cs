using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Motion;

namespace Horyzont.Client.Movement;

/// <summary>The validated state of the own character from a snapshot.</summary>
/// <param name="Planet">Planet whose frame the values are in; in a construct frame the last planet the player was in (K5-20).</param>
/// <param name="ParentSeq">Reparent counter of the entity.</param>
/// <param name="Feet">Feet position.</param>
/// <param name="Rotation">Body rotation.</param>
/// <param name="Velocity">Velocity.</param>
/// <param name="Mode">Movement mode (unknown values are mapped to falling).</param>
/// <param name="LastProcessedInputSeq">Last input the server processed.</param>
/// <param name="SnapshotId">Snapshot number.</param>
/// <param name="ServerTick">Server tick of the snapshot.</param>
public readonly record struct OwnState(
    PlanetId Planet, uint ParentSeq, Vector3 Feet, Quaternion Rotation, Vector3 Velocity, MovementMode Mode, uint LastProcessedInputSeq,
    uint SnapshotId, Tick ServerTick)
{
    /// <summary>Gets the parent frame of the values: the planet, or a construct the player is attached to (K5-20).</summary>
    public FrameRef Frame { get; init; }

    /// <summary>The state as a prediction state, for the comparison in <see cref="PredictionBuffer"/>.</summary>
    /// <param name="pitch">Pitch the client keeps (the snapshot does not carry it).</param>
    /// <param name="onFloor">Whether the client body stands on the floor (the snapshot does not carry it).</param>
    /// <returns>The state.</returns>
    public PredictedState ToPredicted(float pitch, bool onFloor) => new(Feet, Velocity, Rotation, pitch, Mode, onFloor);
}
