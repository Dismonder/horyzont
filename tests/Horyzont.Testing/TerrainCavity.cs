using System.Numerics;

namespace Horyzont.Testing;

/// <summary>
/// A sphere of air inside the solid terrain of <see cref="FakeTerrainService"/> (K6-34, D11): a cave. A ray that starts inside it hits the wall where it leaves the sphere
/// (and misses when it leaves it above the surface of the planet, through an opening to the sky).
/// </summary>
/// <param name="Center">Centre in the frame of the planet.</param>
/// <param name="Radius">Radius in metres.</param>
public sealed record TerrainCavity(Vector3 Center, float Radius);
