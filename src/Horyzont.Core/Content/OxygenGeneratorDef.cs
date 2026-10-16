namespace Horyzont.Core.Content;

/// <summary>The oxygen generator of the hazard content (K6-34, D8): the fuel it burns and how long a cell lasts.</summary>
/// <param name="Fuel">Item key of the fuel cell, e.g. <c>core:fuel_cell</c>.</param>
/// <param name="SecondsPerCell">Seconds one cell burns, 10 to 86 400.</param>
public sealed record OxygenGeneratorDef(string Fuel, int SecondsPerCell);
