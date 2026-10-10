using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Client;

/// <summary>Terrain interest in a planet frame; a zero distance denotes an absent viewer.</summary>
/// <param name="Planet">Planet.</param>
/// <param name="VisualPosition">Visual viewer position.</param>
/// <param name="VisualDistance">Visual distance in metres.</param>
/// <param name="CollisionPosition">Collision viewer position.</param>
/// <param name="CollisionDistance">Collision distance in metres.</param>
public readonly record struct TerrainViewers(
    PlanetId Planet, Vector3 VisualPosition, uint VisualDistance, Vector3 CollisionPosition, uint CollisionDistance);
