using Horyzont.Core.Ships.Flight;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>One pilot input of one client tick (K5-25 decision 11).</summary>
/// <param name="ClientTick">Client tick the input was sampled in.</param>
/// <param name="Control">Axes and buttons.</param>
public readonly record struct ShipInput(uint ClientTick, ShipControl Control);
