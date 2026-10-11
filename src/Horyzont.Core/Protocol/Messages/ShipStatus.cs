using Horyzont.Core.Ids;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>
/// S→C, unreliable, 2 Hz (K5-26 decision 8): the flight status of the ship whose frame the player is in. Wire format after the
/// type and format version: <c>[Ship u64][Tick u64][Fuel f32][FuelCapacity f32][SpeedLimit f32][Flags u8][LaunchBlock u8]</c> (30 bytes, little
/// endian); non-negative finite values, a nonzero ship and a defined <see cref="ShipLaunchBlock"/>.
/// </summary>
/// <param name="Ship">The ship.</param>
/// <param name="Tick">Server tick of the status; the client keeps the newest.</param>
/// <param name="Fuel">Fuel units.</param>
/// <param name="FuelCapacity">Tank capacity.</param>
/// <param name="SpeedLimit">Current speed limit, m/s.</param>
/// <param name="Flags">Flight flags.</param>
/// <param name="LaunchBlock">Why the ship may not take off.</param>
public sealed record ShipStatus(EntityId Ship, Tick Tick, float Fuel, float FuelCapacity, float SpeedLimit, ShipFlightFlags Flags,
    ShipLaunchBlock LaunchBlock) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.ShipStatus;
}
