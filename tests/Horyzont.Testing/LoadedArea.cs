using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Testing;

/// <summary>A loaded terrain area of <see cref="FakeTerrainService"/>.</summary>
public readonly record struct LoadedArea(PlanetId Planet, Vector3 Center, float Radius);
