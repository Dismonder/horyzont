using System.Numerics;

namespace Horyzont.Core.World.Terrain;

/// <summary>Result of a terrain raycast, in the planet frame.</summary>
/// <param name="Position">Hit point.</param>
/// <param name="Normal">Surface normal.</param>
/// <param name="Distance">Distance along the ray in metres.</param>
public readonly record struct TerrainHit(Vector3 Position, Vector3 Normal, float Distance);
