using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;

namespace Horyzont.Core.Intents;

/// <summary>Dig or fill terrain where the ray hits.</summary>
/// <param name="Planet">Planet of the player.</param>
/// <param name="RayOrigin">Ray origin (the eye) in the planet frame.</param>
/// <param name="RayDirection">Normalized direction.</param>
/// <param name="Radius">Tool radius.</param>
/// <param name="Mode">Add or remove.</param>
/// <param name="MaterialId">Material for add; 0 for remove.</param>
public sealed record TerrainToolIntent(
    PlanetId Planet, Vector3 RayOrigin, Vector3 RayDirection, float Radius, TerrainEditMode Mode, byte MaterialId) : Intent;
