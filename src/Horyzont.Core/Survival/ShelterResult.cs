namespace Horyzont.Core.Survival;

/// <summary>The result of the shelter evaluation (K6-34, D11).</summary>
/// <param name="Hits">Rays of the 26 that hit a block or the terrain.</param>
/// <param name="UpHit">Whether the ray straight up (along the up of the planet) hit.</param>
/// <param name="Sheltered">Whether <see cref="UpHit"/> holds and <see cref="Hits"/> reaches <see cref="HazardRules.ShelterRequiredHits"/>.</param>
public readonly record struct ShelterResult(byte Hits, bool UpHit, bool Sheltered);
