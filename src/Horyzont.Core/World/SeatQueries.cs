using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Ids;
using Horyzont.Core.Ships;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.World;

/// <summary>Who sits where (K5-25 decision 10). No state of its own: every answer is computed from <see cref="WorldState"/>.</summary>
public static class SeatQueries
{
    /// <summary>The player, online or offline, sitting in the cell of the construct.</summary>
    /// <param name="state">World state.</param>
    /// <param name="construct">Construct.</param>
    /// <param name="cell">Seat cell.</param>
    /// <param name="occupant">The seated player.</param>
    /// <returns>True when the seat is taken.</returns>
    public static bool TryGetOccupant(WorldState state, EntityId construct, CellPos cell, [NotNullWhen(true)] out PlayerState? occupant)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.TryGetSeated(construct, cell, out occupant);
    }

    /// <summary>The pilot of a ship: the player sitting in its control cockpit (<see cref="ShipStats.ControlCell"/>).</summary>
    /// <param name="state">World state.</param>
    /// <param name="ship">Ship.</param>
    /// <param name="stats">Stats of the ship.</param>
    /// <param name="pilot">The pilot, online or offline.</param>
    /// <returns>True when someone sits in the control cockpit.</returns>
    public static bool TryGetPilot(WorldState state, EntityId ship, ShipStats stats, [NotNullWhen(true)] out PlayerState? pilot)
    {
        ArgumentNullException.ThrowIfNull(stats);
        if (stats.ControlCell is not { } cell)
        {
            pilot = null;
            return false;
        }

        return TryGetOccupant(state, ship, cell, out pilot);
    }
}
