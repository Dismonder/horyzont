using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>
/// S→C, unreliable, only to the pilot with its snapshot: the server ship state after the step of input
/// <paramref name="LastProcessedSeq"/> and the step inputs the client cannot know (K5-25 decision 14).
/// </summary>
/// <param name="Ship">The ship.</param>
/// <param name="LastProcessedSeq">Input slot whose step produced this state.</param>
/// <param name="ParentSeq">Reparent counter of the ship.</param>
/// <param name="Position">Position in the parent frame.</param>
/// <param name="Rotation">Rotation in the parent frame.</param>
/// <param name="LinearVelocity">Linear velocity in the parent frame.</param>
/// <param name="AngularVelocity">Angular velocity in construct axes.</param>
/// <param name="Fuel">Fuel units.</param>
/// <param name="Flags">Flight flags.</param>
/// <param name="Gate">Terrain gate of the step.</param>
/// <param name="LaunchBlock">Launch block of the step.</param>
/// <param name="HorizontalSpeedCap">Horizontal speed cap of the step (Y11); positive infinity = none.</param>
public sealed record ShipCorrection(
    EntityId Ship, uint LastProcessedSeq, uint ParentSeq, Vector3 Position, Quaternion Rotation, Vector3 LinearVelocity,
    Vector3 AngularVelocity, float Fuel, ShipFlightFlags Flags, ShipTerrainGate Gate, ShipLaunchBlock LaunchBlock,
    float HorizontalSpeedCap) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.ShipCorrection;
}
