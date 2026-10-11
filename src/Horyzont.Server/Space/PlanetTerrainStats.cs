using Horyzont.Core.Ids;

namespace Horyzont.Server.Space;

/// <summary>Statistics of one active planet terrain for the over-budget log (K5-22 decision 7, spec 02:211).</summary>
/// <param name="Planet">The planet.</param>
/// <param name="Players">Players whose region is the planet.</param>
/// <param name="Viewers">Voxel viewers of the planet (characters and ships).</param>
/// <param name="BlocksInMemory">Data blocks the terrain holds.</param>
/// <param name="UpdateMilliseconds">Time of the last terrain update (Voxel Tools statistics).</param>
public readonly record struct PlanetTerrainStats(PlanetId Planet, int Players, int Viewers, int BlocksInMemory, double UpdateMilliseconds);
