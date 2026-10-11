using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Ships;

namespace Horyzont.Server.Space;

/// <summary>
/// The ship demand of K5-24 (K5-22 decision 2b, README Y11): a ship-mode construct demands the terrain of its planet while it is not
/// parked. A ship without a flight state has not moved since the switch to the ship mode, so it stands parked. Allocation-free.
/// </summary>
/// <param name="state">The world (ship states).</param>
public sealed class UnparkedShipDemand(WorldState state) : IShipDemandSource
{
    private readonly WorldState _state = state ?? throw new ArgumentNullException(nameof(state));

    /// <inheritdoc />
    public bool Demands(Construct ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return ship.Mode == ConstructMode.Ship
            && _state.ShipStates.TryGet(ship.Id, out var record)
            && (record.Flags & ShipStateFlags.Parked) == 0;
    }
}
