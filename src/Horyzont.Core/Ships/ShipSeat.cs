using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Ships;

/// <summary>A seat of a ship: the cell of a cockpit (pilot) or seat block.</summary>
/// <param name="Cell">Cell of the seat block; a seat is a cell, not a separate entity.</param>
/// <param name="Role">Pilot for a cockpit, otherwise passenger or navigator.</param>
public readonly record struct ShipSeat(CellPos Cell, SeatRole Role);
