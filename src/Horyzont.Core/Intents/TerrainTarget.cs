using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Core.Intents;

/// <summary>Place on the terrain where the ray hits; creates a new construct.</summary>
/// <param name="Planet">Planet of the player.</param>
/// <param name="RayOrigin">Ray origin (the eye) in the planet frame.</param>
/// <param name="RayDirection">Normalized direction.</param>
/// <param name="LookForward">Normalized look direction used to orient the new construct.</param>
public sealed record TerrainTarget(PlanetId Planet, Vector3 RayOrigin, Vector3 RayDirection, Vector3 LookForward) : BlockTarget;
